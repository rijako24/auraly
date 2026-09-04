using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Host;

public sealed record PosLocalWorkSession(
    Guid WorkSessionId,
    Guid UserId,
    DateTimeOffset OpenedAt);

/// <summary>
/// Canonical owner of an enrolled device's operational sessions. Authentication
/// sessions merely reference these rows; they never create or close them.
/// </summary>
public sealed class PosLocalWorkSessionStore(
    string connectionString,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    PosEdgeRuntimeContext runtime)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await PosUnifiedOutboxSchema.EnsureCreatedAsync(connectionString, cancellationToken);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PosLocalWorkSessions(
                WorkSessionId TEXT NOT NULL PRIMARY KEY,
                TenantId TEXT NOT NULL,
                BusinessId TEXT NOT NULL,
                DeviceId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                OpenedAt TEXT NOT NULL,
                ClosedAt TEXT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureTenantScopeAsync(connection, cancellationToken);
        await MigrateLegacyActiveSessionsAsync(connection, cancellationToken);
    }

    public async Task<PosLocalWorkSession> OpenOrResumeAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using (var current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText = """
                SELECT WorkSessionId,OpenedAt
                FROM PosLocalWorkSessions
                WHERE TenantId=$tenant AND BusinessId=$business
                  AND DeviceId=$device AND UserId=$user
                  AND ClosedAt IS NULL
                LIMIT 1;
                """;
            AddScope(current, userId);
            await using var reader = await current.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existing = new PosLocalWorkSession(
                    Guid.Parse(reader.GetString(0)), userId,
                    DateTimeOffset.Parse(reader.GetString(1)));
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }
        }

        var opened = new PosLocalWorkSession(
            ids.NewId(), userId, timeProvider.GetUtcNow());
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO PosLocalWorkSessions(
                    WorkSessionId,TenantId,BusinessId,DeviceId,UserId,OpenedAt,ClosedAt)
                VALUES($id,$tenant,$business,$device,$user,$opened,NULL);
                """;
            insert.Parameters.AddWithValue("$id", opened.WorkSessionId.ToString("D"));
            insert.Parameters.AddWithValue("$opened", opened.OpenedAt.ToString("O"));
            AddScope(insert, userId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        var payload = JsonSerializer.Serialize(new RegisterDeviceWorkSessionRequest(
            userId, opened.WorkSessionId, runtime.BusinessId.Value, opened.OpenedAt), Json);
        await using (var enqueue = connection.CreateCommand())
        {
            enqueue.Transaction = transaction;
            enqueue.CommandText = """
                INSERT INTO Outbox(
                    MessageId,DocumentId,WorkSessionId,Type,Payload,Status,
                    AttemptCount,CreatedAt)
                VALUES($id,$id,$id,$type,$payload,'Pending',0,$opened)
                ON CONFLICT(DocumentId) DO NOTHING;
                """;
            enqueue.Parameters.AddWithValue("$id", opened.WorkSessionId.ToString("D"));
            enqueue.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionOpened);
            enqueue.Parameters.AddWithValue("$payload", payload);
            enqueue.Parameters.AddWithValue("$opened", opened.OpenedAt.ToString("O"));
            await enqueue.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return opened;
    }

    public async Task MarkClosedAsync(
        Guid workSessionId,
        Guid userId,
        DateTimeOffset closedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using (var close = connection.CreateCommand())
        {
            close.Transaction = transaction;
            close.CommandText = """
                UPDATE PosLocalWorkSessions SET ClosedAt=COALESCE(ClosedAt,$closed)
                WHERE WorkSessionId=$id AND TenantId=$tenant AND BusinessId=$business
                  AND DeviceId=$device AND UserId=$user;
                """;
            close.Parameters.AddWithValue("$id", workSessionId.ToString("D"));
            close.Parameters.AddWithValue("$closed", closedAt.ToString("O"));
            AddScope(close, userId);
            if (await close.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException(
                    "La sesión operativa local no pertenece al usuario y dispositivo activos.");
        }
        await using (var legacy = connection.CreateCommand())
        {
            legacy.Transaction = transaction;
            legacy.CommandText = """
                INSERT INTO PosClosedWorkSessions(WorkSessionId,UserId,ClosedAt)
                VALUES($id,$user,$closed)
                ON CONFLICT(WorkSessionId) DO NOTHING;
                """;
            legacy.Parameters.AddWithValue("$id", workSessionId.ToString("D"));
            legacy.Parameters.AddWithValue("$user", userId.ToString("D"));
            legacy.Parameters.AddWithValue("$closed", closedAt.ToString("O"));
            await legacy.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var detach = connection.CreateCommand())
        {
            detach.Transaction = transaction;
            detach.CommandText = """
                UPDATE PosLocalUserSessions SET WorkSessionId=$empty
                WHERE WorkSessionId=$id AND UserId=$user AND EndedAt IS NULL;
                """;
            detach.Parameters.AddWithValue("$empty", Guid.Empty.ToString("D"));
            detach.Parameters.AddWithValue("$id", workSessionId.ToString("D"));
            detach.Parameters.AddWithValue("$user", userId.ToString("D"));
            await detach.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private void AddScope(SqliteCommand command, Guid userId)
    {
        command.Parameters.AddWithValue("$tenant", runtime.TenantId.Value.ToString("D"));
        command.Parameters.AddWithValue("$business", runtime.BusinessId.Value.ToString("D"));
        command.Parameters.AddWithValue("$device", runtime.DeviceId.Value.ToString("D"));
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
    }

    private async Task EnsureTenantScopeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var hasTenant = false;
        await using (var columns = connection.CreateCommand())
        {
            columns.CommandText = "PRAGMA table_info(PosLocalWorkSessions);";
            await using var reader = await columns.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                hasTenant |= string.Equals(reader.GetString(1), "TenantId", StringComparison.OrdinalIgnoreCase);
        }
        if (!hasTenant)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE PosLocalWorkSessions ADD COLUMN TenantId TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var scope = connection.CreateCommand();
        scope.CommandText = """
            UPDATE PosLocalWorkSessions SET TenantId=$tenant
            WHERE TenantId IS NULL OR TenantId='';
            DROP INDEX IF EXISTS UX_PosLocalWorkSessions_OpenScope;
            CREATE UNIQUE INDEX UX_PosLocalWorkSessions_OpenScope
                ON PosLocalWorkSessions(TenantId,BusinessId,DeviceId,UserId)
                WHERE ClosedAt IS NULL;
            """;
        scope.Parameters.AddWithValue("$tenant", runtime.TenantId.Value.ToString("D"));
        await scope.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MigrateLegacyActiveSessionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = """
                SELECT COUNT(1) FROM sqlite_master
                WHERE type='table' AND name='PosLocalUserSessions';
                """;
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken)) == 0)
                return;
        }
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = """
                INSERT OR IGNORE INTO PosLocalWorkSessions(
                    WorkSessionId,TenantId,BusinessId,DeviceId,UserId,OpenedAt,ClosedAt)
                SELECT legacy.WorkSessionId,$tenant,$business,$device,legacy.UserId,
                       legacy.StartedAt,NULL
                FROM (
                    SELECT s.WorkSessionId,s.UserId,s.StartedAt,
                           ROW_NUMBER() OVER(PARTITION BY s.UserId ORDER BY s.StartedAt DESC) AS Position
                    FROM PosLocalUserSessions s
                    WHERE s.EndedAt IS NULL
                      AND s.WorkSessionId<>'00000000-0000-0000-0000-000000000000'
                      AND NOT EXISTS (
                          SELECT 1 FROM PosClosedWorkSessions c
                          WHERE c.WorkSessionId=s.WorkSessionId)
                ) legacy
                WHERE legacy.Position=1;
                """;
            migrate.Parameters.AddWithValue("$business", runtime.BusinessId.Value.ToString("D"));
            migrate.Parameters.AddWithValue("$device", runtime.DeviceId.Value.ToString("D"));
            migrate.Parameters.AddWithValue("$tenant", runtime.TenantId.Value.ToString("D"));
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }

        var missing = new List<PosLocalWorkSession>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = """
                SELECT s.WorkSessionId,s.UserId,s.OpenedAt
                FROM PosLocalWorkSessions s
                WHERE s.TenantId=$tenant AND s.BusinessId=$business
                  AND s.DeviceId=$device AND s.ClosedAt IS NULL AND NOT EXISTS(
                    SELECT 1 FROM Outbox o
                    WHERE o.DocumentId=s.WorkSessionId
                      AND o.Type=$type)
                ORDER BY s.OpenedAt,s.WorkSessionId;
                """;
            read.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionOpened);
            read.Parameters.AddWithValue("$tenant", runtime.TenantId.Value.ToString("D"));
            read.Parameters.AddWithValue("$business", runtime.BusinessId.Value.ToString("D"));
            read.Parameters.AddWithValue("$device", runtime.DeviceId.Value.ToString("D"));
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                missing.Add(new PosLocalWorkSession(
                    Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                    DateTimeOffset.Parse(reader.GetString(2))));
        }
        long minimumSequence;
        await using (var minimum = connection.CreateCommand())
        {
            minimum.CommandText = "SELECT COALESCE(MIN(LocalSequence),0) FROM Outbox;";
            minimumSequence = Convert.ToInt64(
                await minimum.ExecuteScalarAsync(cancellationToken));
        }
        var sequence = Math.Min(0, minimumSequence) - missing.Count;
        foreach (var legacy in missing)
        {
            var payload = JsonSerializer.Serialize(new RegisterDeviceWorkSessionRequest(
                legacy.UserId, legacy.WorkSessionId, runtime.BusinessId.Value,
                legacy.OpenedAt), Json);
            await using var enqueue = connection.CreateCommand();
            enqueue.CommandText = """
                INSERT INTO Outbox(
                    MessageId,DocumentId,WorkSessionId,LocalSequence,Type,Payload,
                    Status,AttemptCount,CreatedAt)
                VALUES($id,$id,$id,$sequence,$type,$payload,'Pending',0,$opened)
                ON CONFLICT(DocumentId) DO NOTHING;
                """;
            enqueue.Parameters.AddWithValue("$id", legacy.WorkSessionId.ToString("D"));
            enqueue.Parameters.AddWithValue("$sequence", sequence++);
            enqueue.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionOpened);
            enqueue.Parameters.AddWithValue("$payload", payload);
            enqueue.Parameters.AddWithValue("$opened", legacy.OpenedAt.ToString("O"));
            await enqueue.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

public sealed class PosWorkSessionOpenUploader(
    string connectionString,
    HttpClient http,
    PosDeviceCredentials credentials,
    TimeProvider timeProvider,
    PosSynchronizationEventLog events)
{
    public async Task<bool> UploadNextAsync(CancellationToken cancellationToken = default)
    {
        var item = await ClaimAsync(cancellationToken);
        if (item is null) return false;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/pos/v1/work-sessions/opened");
            request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
            request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
            request.Content = new StringContent(item.Value.Payload, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var returned = await response.Content.ReadFromJsonAsync<WorkSessionView>(
                cancellationToken: cancellationToken);
            if (returned?.WorkSessionId != item.Value.WorkSessionId)
                throw new InvalidDataException(
                    "Auraly Server no confirmó el identificador exacto de la sesión local.");
            await UpdateAsync(item.Value.WorkSessionId, "Uploaded", null, null, cancellationToken);
            events.Record("Success", "WorkSession", "Sesión de caja local subida",
                item.Value.WorkSessionId.ToString("D"));
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException)
        {
            var seconds = Math.Min(300, 5 * Math.Pow(2, Math.Clamp(item.Value.Attempts, 0, 6)));
            await UpdateAsync(item.Value.WorkSessionId, "RetryScheduled",
                timeProvider.GetUtcNow().AddSeconds(seconds), error.Message, cancellationToken);
            events.Record("Warning", "WorkSession", "Sesión de caja pendiente",
                $"{item.Value.WorkSessionId:D} · {error.Message}");
        }
        return true;
    }

    private async Task<(Guid WorkSessionId, string Payload, int Attempts)?> ClaimAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT DocumentId,Payload,AttemptCount FROM Outbox
            WHERE Type=$type
              AND ((Status IN ('Pending','RetryScheduled') AND
                    (NextAttemptAt IS NULL OR NextAttemptAt<=$now))
                   OR (Status='Uploading' AND LastAttemptAt<$stale))
              AND NOT EXISTS (
                  SELECT 1 FROM Outbox prior
                  WHERE prior.Status<>'Uploaded'
                    AND prior.LocalSequence<Outbox.LocalSequence)
            ORDER BY LocalSequence LIMIT 1;
            """;
        read.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionOpened);
        read.Parameters.AddWithValue("$now", now.ToString("O"));
        read.Parameters.AddWithValue("$stale", now.AddMinutes(-2).ToString("O"));
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var item = (Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2) + 1);
        await reader.DisposeAsync();
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE Outbox SET Status='Uploading',AttemptCount=AttemptCount+1,LastAttemptAt=$now
            WHERE DocumentId=$id AND Type=$type;
            """;
        update.Parameters.AddWithValue("$now", now.ToString("O"));
        update.Parameters.AddWithValue("$id", item.Item1.ToString("D"));
        update.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionOpened);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item;
    }

    private async Task UpdateAsync(
        Guid id, string status, DateTimeOffset? next, string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Outbox SET Status=$status,NextAttemptAt=$next,LastError=$error,
                UploadedAt=CASE WHEN $status='Uploaded' THEN $now ELSE UploadedAt END
            WHERE DocumentId=$id AND Type=$type;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$next", (object?)next?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionOpened);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
