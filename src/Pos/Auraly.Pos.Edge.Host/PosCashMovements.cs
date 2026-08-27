using System.Data;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.Sqlite;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record QueueLocalCashMovementRequest(
    Guid DocumentId,
    Guid ReasonId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Reference,
    string? Notes,
    Guid? CostCenterId);

public sealed record LocalCashMovementAcceptance(
    Guid DocumentId,
    string Status,
    bool IdempotentReplay);

public sealed record PosCashMovementSynchronizationStatus(
    int PendingCount,
    DateTimeOffset? OldestPendingAt,
    string? LastError);

public sealed class PosCashMovementStore(
    string connectionString,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await PosUnifiedOutboxSchema.EnsureCreatedAsync(
            connectionString, cancellationToken);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PosCashMovementReasons(
              ReasonId TEXT PRIMARY KEY,
              BusinessId TEXT NOT NULL,
              Direction TEXT NOT NULL,
              IsActive INTEGER NOT NULL,
              Payload TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_PosCashMovementReasons_Business_Direction
              ON PosCashMovementReasons(BusinessId,Direction,IsActive);
            CREATE TABLE IF NOT EXISTS PosCashMovements(
              DocumentId TEXT PRIMARY KEY,
              Payload TEXT NOT NULL,
              CreatedAt TEXT NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReplaceReasonsAsync(
        Guid businessId,
        IReadOnlyCollection<CashMovementReasonView> reasons,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM PosCashMovementReasons WHERE BusinessId=$business;";
            delete.Parameters.AddWithValue("$business", businessId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var reason in reasons)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO PosCashMovementReasons(
                  ReasonId,BusinessId,Direction,IsActive,Payload,UpdatedAt)
                VALUES($id,$business,$direction,$active,$payload,$now);
                """;
            insert.Parameters.AddWithValue("$id", reason.ReasonId.ToString("D"));
            insert.Parameters.AddWithValue("$business", businessId.ToString("D"));
            insert.Parameters.AddWithValue("$direction", reason.Direction);
            insert.Parameters.AddWithValue("$active", reason.IsActive ? 1 : 0);
            insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(reason, Json));
            insert.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CashMovementReasonView>> ListReasonsAsync(
        Guid businessId,
        string direction,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Payload FROM PosCashMovementReasons
            WHERE BusinessId=$business AND Direction=$direction AND IsActive=1
            ORDER BY json_extract(Payload,'$.name');
            """;
        command.Parameters.AddWithValue("$business", businessId.ToString("D"));
        command.Parameters.AddWithValue("$direction", direction);
        var values = new List<CashMovementReasonView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(JsonSerializer.Deserialize<CashMovementReasonView>(
                           reader.GetString(0), Json)
                       ?? throw new InvalidDataException(
                           "A cached cash-movement reason is invalid."));
        return values;
    }

    public async Task<decimal> ReadWorkSessionNetCashAsync(
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.Payload,r.Direction
            FROM PosCashMovements o
            JOIN PosCashMovementReasons r
              ON r.ReasonId=json_extract(o.Payload,'$.movement.reasonId');
            """;
        decimal total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = JsonSerializer.Deserialize<DeviceCashMovementRequest>(
                reader.GetString(0), Json)
                ?? throw new InvalidDataException("Un movimiento local de caja no es válido.");
            if (value.Movement.WorkSessionId != workSessionId) continue;
            total += reader.GetString(1) == CashMovementDirections.In
                ? value.Movement.Amount
                : -value.Movement.Amount;
        }
        return total;
    }

    public async Task<PosCashMovementSynchronizationStatus> ReadOutboxStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CreatedAt,LastError FROM Outbox
            WHERE Type=$type AND Status<>'Uploaded' ORDER BY CreatedAt;
            """;
        command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CashMovement);
        var count = 0;
        DateTimeOffset? oldest = null;
        string? error = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            count++;
            oldest ??= DateTimeOffset.Parse(reader.GetString(0));
            if (!reader.IsDBNull(1)) error = reader.GetString(1);
        }
        return new PosCashMovementSynchronizationStatus(count, oldest, error);
    }

    public async Task<bool> HasPendingForWorkSessionAsync(
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Payload FROM Outbox
            WHERE Type=$type AND Status<>'Uploaded' AND WorkSessionId=$session;
            """;
        command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CashMovement);
        command.Parameters.AddWithValue("$session", workSessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = JsonSerializer.Deserialize<DeviceCashMovementRequest>(
                reader.GetString(0), Json)
                ?? throw new InvalidDataException("Un movimiento local de caja no es válido.");
            if (value.Movement.WorkSessionId == workSessionId) return true;
        }
        return false;
    }

    public async Task<LocalCashMovementAcceptance> QueueAsync(
        Guid businessId,
        Guid workSessionId,
        Guid userId,
        QueueLocalCashMovementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DocumentId == Guid.Empty || request.ReasonId == Guid.Empty ||
            request.Amount <= 0)
            throw new ArgumentException(
                "El motivo, el valor y el identificador del movimiento son obligatorios.");
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        CashMovementReasonView reason;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT Payload FROM PosCashMovementReasons
                WHERE ReasonId=$reason AND BusinessId=$business AND IsActive=1;
                """;
            read.Parameters.AddWithValue("$reason", request.ReasonId.ToString("D"));
            read.Parameters.AddWithValue("$business", businessId.ToString("D"));
            var payload = await read.ExecuteScalarAsync(cancellationToken) as string
                ?? throw new ArgumentException(
                    "El motivo no esta disponible en la caja local.");
            reason = JsonSerializer.Deserialize<CashMovementReasonView>(payload, Json)
                ?? throw new InvalidDataException(
                    "El motivo almacenado en la caja no es valido.");
        }
        if (reason.RequiresReference && string.IsNullOrWhiteSpace(request.Reference))
            throw new ArgumentException("El motivo seleccionado exige una referencia.");

        var movement = new ConfirmCashMovementRequest(
            request.DocumentId,
            businessId,
            workSessionId,
            request.ReasonId,
            request.Amount,
            request.OccurredAt,
            request.Reference,
            request.Notes,
            request.CostCenterId);
        var payloadJson = JsonSerializer.Serialize(
            new DeviceCashMovementRequest(userId, movement), Json);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO PosCashMovements(DocumentId,Payload,CreatedAt)
            VALUES($id,$payload,$now)
            ON CONFLICT(DocumentId) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$id", request.DocumentId.ToString("D"));
        insert.Parameters.AddWithValue("$payload", payloadJson);
        insert.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
        var inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 0)
        {
            await using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText =
                "SELECT Payload FROM PosCashMovements WHERE DocumentId=$id;";
            existing.Parameters.AddWithValue("$id", request.DocumentId.ToString("D"));
            if (!string.Equals(
                    await existing.ExecuteScalarAsync(cancellationToken) as string,
                    payloadJson,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "El identificador ya fue usado para otro movimiento.");
        }
        await using (var enqueue = connection.CreateCommand())
        {
            enqueue.Transaction = transaction;
            enqueue.CommandText = """
                INSERT INTO Outbox(
                  MessageId,DocumentId,WorkSessionId,Type,Payload,Status,
                  AttemptCount,CreatedAt)
                VALUES($id,$id,$session,$type,$payload,'Pending',0,$now)
                ON CONFLICT(DocumentId) DO NOTHING;
                """;
            enqueue.Parameters.AddWithValue("$id", request.DocumentId.ToString("D"));
            enqueue.Parameters.AddWithValue("$session", workSessionId.ToString("D"));
            enqueue.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CashMovement);
            enqueue.Parameters.AddWithValue("$payload", payloadJson);
            enqueue.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
            await enqueue.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new LocalCashMovementAcceptance(
            request.DocumentId, "PendingSynchronization", inserted == 0);
    }

    public async Task<(Guid DocumentId, string Payload, int AttemptCount)?> ClaimAsync(
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT DocumentId,Payload,AttemptCount
            FROM Outbox
            WHERE Type=$type AND ((Status IN ('Pending','RetryScheduled')
                   AND (NextAttemptAt IS NULL OR NextAttemptAt<=$now))
               OR (Status='Uploading' AND LastAttemptAt<$stale))
              AND NOT EXISTS
              (
                SELECT 1 FROM Outbox prior
                WHERE Outbox.WorkSessionId IS NOT NULL
                  AND prior.WorkSessionId=Outbox.WorkSessionId
                  AND prior.Status<>'Uploaded'
                  AND (prior.CreatedAt<Outbox.CreatedAt OR
                       (prior.CreatedAt=Outbox.CreatedAt AND prior.MessageId<Outbox.MessageId))
              )
            ORDER BY CreatedAt LIMIT 1;
            """;
        read.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CashMovement);
        read.Parameters.AddWithValue("$now", now.ToString("O"));
        read.Parameters.AddWithValue("$stale", now.AddMinutes(-2).ToString("O"));
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var item = (Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2));
        await reader.DisposeAsync();
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE Outbox SET Status='Uploading',
              AttemptCount=AttemptCount+1,LastAttemptAt=$now
            WHERE DocumentId=$id AND Type=$type;
            """;
        update.Parameters.AddWithValue("$now", now.ToString("O"));
        update.Parameters.AddWithValue("$id", item.Item1.ToString("D"));
        update.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CashMovement);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item;
    }

    public Task MarkUploadedAsync(Guid documentId, CancellationToken ct = default) =>
        UpdateAsync(documentId, "Uploaded", null, null, ct);

    public Task ScheduleRetryAsync(
        Guid documentId, int attempts, string error,
        CancellationToken ct = default)
    {
        var seconds = Math.Min(300, 5 * Math.Pow(2, Math.Clamp(attempts, 0, 6)));
        return UpdateAsync(
            documentId, "RetryScheduled",
            timeProvider.GetUtcNow().AddSeconds(seconds), error, ct);
    }

    private async Task UpdateAsync(
        Guid documentId, string status, DateTimeOffset? next, string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Outbox
            SET Status=$status,NextAttemptAt=$next,LastError=$error,
                UploadedAt=CASE WHEN $status='Uploaded' THEN $now ELSE UploadedAt END
            WHERE DocumentId=$id AND Type=$type;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$next", (object?)next?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", documentId.ToString("D"));
        command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CashMovement);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class PosCashMovementServerClient(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosCashMovementStore store,
    PosSynchronizationEventLog events)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public async Task RefreshReasonsAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        using var request = Request(
            HttpMethod.Get,
            "/api/pos/v1/cash-movement-reasons?businessId=" +
            businessId.ToString("D"));
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var reasons = await response.Content.ReadFromJsonAsync<CashMovementReasonView[]>(
                          cancellationToken: cancellationToken)
                      ?? [];
        await store.ReplaceReasonsAsync(businessId, reasons, cancellationToken);
    }

    public async Task<bool> UploadNextAsync(
        CancellationToken cancellationToken = default)
    {
        var item = await store.ClaimAsync(cancellationToken);
        if (item is null) return false;
        using var request = Request(HttpMethod.Post, "/api/pos/v1/cash-movements");
        request.Headers.Add("Idempotency-Key", item.Value.DocumentId.ToString("D"));
        request.Content = new StringContent(
            item.Value.Payload, Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await store.MarkUploadedAsync(item.Value.DocumentId, cancellationToken);
                events.Record("Success", "CashMovement", "Movimiento de caja subido",
                    item.Value.DocumentId.ToString("D"));
            }
            else
            {
                await store.ScheduleRetryAsync(
                    item.Value.DocumentId, item.Value.AttemptCount,
                    "Auraly Server respondio HTTP " + (int)response.StatusCode,
                    cancellationToken);
                events.Record("Warning", "CashMovement", "Movimiento de caja pendiente",
                    $"{item.Value.DocumentId:D} · HTTP {(int)response.StatusCode}");
            }
        }
        catch (HttpRequestException exception)
        {
            await store.ScheduleRetryAsync(
                item.Value.DocumentId, item.Value.AttemptCount,
                exception.Message, cancellationToken);
            events.Record("Warning", "CashMovement", "Movimiento de caja pendiente",
                $"{item.Value.DocumentId:D} · {exception.Message}");
        }
        return true;
    }

    private HttpRequestMessage Request(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        return request;
    }
}

internal sealed class PosCashMovementStorageInitializer(
    PosCashMovementStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
