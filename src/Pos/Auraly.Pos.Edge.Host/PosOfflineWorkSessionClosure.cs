using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Host;

public sealed record PosClosureOutboxStatus(
    int PendingCount,
    DateTimeOffset? OldestPendingAt,
    string? LastError);

internal sealed record PosQueuedWorkSessionClosure(
    Guid OperationId,
    WorkSessionClosureView Closure,
    DeviceCloseWorkSessionRequest Request);

public sealed class PosOfflineWorkSessionClosureStore(
    string connectionString,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await PosUnifiedOutboxSchema.EnsureCreatedAsync(
            connectionString, cancellationToken);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PosWorkSessionClosures(
              OperationId TEXT NOT NULL PRIMARY KEY,
              WorkSessionId TEXT NOT NULL UNIQUE,
              Payload TEXT NOT NULL,
              CreatedAt TEXT NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal async Task<WorkSessionClosureView> QueueAsync(
        PosQueuedWorkSessionClosure value,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(value, Json);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO PosWorkSessionClosures(
              OperationId,WorkSessionId,Payload,CreatedAt)
            VALUES($operation,$session,$payload,$now)
            ON CONFLICT DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$operation", value.OperationId.ToString("D"));
        insert.Parameters.AddWithValue("$session", value.Closure.WorkSessionId.ToString("D"));
        insert.Parameters.AddWithValue("$payload", payload);
        insert.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
        var inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 0)
        {
            await using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = """
                SELECT OperationId,Payload FROM PosWorkSessionClosures
                WHERE OperationId=$operation OR WorkSessionId=$session
                ORDER BY CASE WHEN OperationId=$operation THEN 0 ELSE 1 END
                LIMIT 1;
                """;
            existing.Parameters.AddWithValue("$operation", value.OperationId.ToString("D"));
            existing.Parameters.AddWithValue("$session", value.Closure.WorkSessionId.ToString("D"));
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            var found = await reader.ReadAsync(cancellationToken);
            var existingOperationId = found ? Guid.Parse(reader.GetString(0)) : Guid.Empty;
            var existingValue = !found
                ? null
                : JsonSerializer.Deserialize<PosQueuedWorkSessionClosure>(reader.GetString(1), Json);
            await reader.DisposeAsync();
            if (existingValue is null)
                throw new InvalidOperationException(
                    "No fue posible recuperar el cierre ya guardado para esta sesión.");
            if (existingOperationId == value.OperationId && !string.Equals(
                    JsonSerializer.Serialize(existingValue.Request, Json),
                    JsonSerializer.Serialize(value.Request, Json),
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "El identificador del cierre ya fue usado con otro conteo.");
            // One work session can only have one closure. If a previous process
            // persisted it and stopped before ending the local login, recover
            // that exact closure instead of creating or recalculating another.
            value = existingValue;
        }
        await using (var enqueue = connection.CreateCommand())
        {
            enqueue.Transaction = transaction;
            enqueue.CommandText = """
                INSERT INTO Outbox(
                  MessageId,DocumentId,WorkSessionId,Type,Payload,Status,
                  AttemptCount,CreatedAt)
                VALUES($operation,$operation,$session,$type,$payload,'Pending',0,$now)
                ON CONFLICT(DocumentId) DO NOTHING;
                """;
            enqueue.Parameters.AddWithValue("$operation", value.OperationId.ToString("D"));
            enqueue.Parameters.AddWithValue("$session", value.Closure.WorkSessionId.ToString("D"));
            enqueue.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionClosure);
            enqueue.Parameters.AddWithValue("$payload", payload);
            enqueue.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
            await enqueue.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return value.Closure;
    }

    internal async Task<(Guid OperationId, PosQueuedWorkSessionClosure Value, int Attempts)?> ClaimAsync(
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
        read.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionClosure);
        read.Parameters.AddWithValue("$now", now.ToString("O"));
        read.Parameters.AddWithValue("$stale", now.AddMinutes(-2).ToString("O"));
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var operationId = Guid.Parse(reader.GetString(0));
        var value = JsonSerializer.Deserialize<PosQueuedWorkSessionClosure>(reader.GetString(1), Json)
            ?? throw new InvalidDataException("El cierre local almacenado no es válido.");
        var attempts = reader.GetInt32(2) + 1;
        await reader.DisposeAsync();
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE Outbox
            SET Status='Uploading',AttemptCount=AttemptCount+1,LastAttemptAt=$now
            WHERE DocumentId=$operation AND Type=$type;
            """;
        update.Parameters.AddWithValue("$now", now.ToString("O"));
        update.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        update.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionClosure);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (operationId, value, attempts);
    }

    public Task MarkUploadedAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        UpdateAsync(operationId, "Uploaded", null, null, cancellationToken);

    public Task ScheduleRetryAsync(
        Guid operationId,
        int attempts,
        string error,
        CancellationToken cancellationToken = default)
    {
        var seconds = Math.Min(300, 5 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 6)));
        return UpdateAsync(
            operationId,
            "RetryScheduled",
            timeProvider.GetUtcNow().AddSeconds(seconds),
            error,
            cancellationToken);
    }

    public async Task<PosClosureOutboxStatus> ReadStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CreatedAt,LastError FROM Outbox
            WHERE Type=$type AND Status<>'Uploaded' ORDER BY CreatedAt;
            """;
        command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionClosure);
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
        return new PosClosureOutboxStatus(count, oldest, error);
    }

    private async Task UpdateAsync(
        Guid operationId,
        string status,
        DateTimeOffset? nextAttemptAt,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Outbox
            SET Status=$status,NextAttemptAt=$next,LastError=$error,
                UploadedAt=CASE WHEN $status='Uploaded' THEN $now ELSE UploadedAt END
            WHERE DocumentId=$operation AND Type=$type;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$next", (object?)nextAttemptAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.WorkSessionClosure);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class PosOfflineWorkSessionClosureService(
    PosEdgeSaleStore sales,
    PosCashMovementStore cashMovements,
    PosLocalIdentityStore identities,
    PosOfflineWorkSessionClosureStore store,
    PosEdgeRuntimeContext runtime,
    PosWorkstationIdentity workstation,
    TimeProvider timeProvider)
{
    public async Task<WorkSessionClosurePreviewView> PreviewAsync(
        PosLocalUserSession session,
        CancellationToken cancellationToken)
    {
        var localSales = await sales.ReadWorkSessionSalesAsync(
            session.WorkSessionId, cancellationToken);
        var otherCash = await cashMovements.ReadWorkSessionNetCashAsync(
            session.WorkSessionId, cancellationToken);
        var openedAt = await identities.WorkSessionOpenedAtAsync(
            session.WorkSessionId, cancellationToken);
        var lastActivity = localSales.Count == 0
            ? openedAt
            : localSales.Max(value => value.IssuedAt);
        var totals = PaymentTotals(localSales, otherCash, null);
        return new WorkSessionClosurePreviewView(
            session.WorkSessionId,
            runtime.BusinessId.Value,
            workstation.BusinessName,
            runtime.WarehouseId.Value,
            workstation.WarehouseName,
            session.UserId,
            session.DisplayName,
            openedAt,
            lastActivity,
            localSales.Sum(value => value.Total),
            0,
            otherCash,
            localSales.Sum(value => value.Total) + otherCash,
            totals.Single(value => value.PaymentMethodCode == "Cash").NetAmount,
            totals,
            localSales.Count,
            localSales.Count(value => value.CreditAmount > 0),
            localSales.Sum(value => value.CreditAmount),
            0);
    }

    public async Task<WorkSessionClosureView> CloseAsync(
        PosLocalUserSession session,
        CloseLocalWorkSessionRequest input,
        Guid authorizedByUserId,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(session, cancellationToken);
        var closedAt = timeProvider.GetUtcNow();
        var totals = PaymentTotals(
            await sales.ReadWorkSessionSalesAsync(session.WorkSessionId, cancellationToken),
            preview.TotalOther,
            input.PaymentCounts);
        var countedCash = totals.Single(value => value.PaymentMethodCode == "Cash").CountedAmount
            ?? input.CountedCash;
        var closure = new WorkSessionClosureView(
            input.OperationId,
            preview.WorkSessionId,
            preview.BusinessId,
            preview.BusinessName,
            preview.WarehouseId,
            preview.WarehouseName,
            preview.UserId,
            preview.UserName,
            runtime.DeviceId.Value,
            preview.OpenedAt,
            closedAt,
            preview.TotalSales,
            preview.TotalRefunds,
            preview.TotalOther,
            preview.NetAmount,
            preview.ExpectedCash,
            countedCash,
            countedCash - preview.ExpectedCash,
            input.Note,
            totals,
            preview.SalesCount,
            preview.CreditSalesCount,
            preview.CreditSalesAmount,
            preview.ReturnCount);
        var queued = await store.QueueAsync(
            new PosQueuedWorkSessionClosure(
                input.OperationId,
                closure,
                new DeviceCloseWorkSessionRequest(
                    session.UserId,
                    session.WorkSessionId,
                    countedCash,
                    input.Note,
                    authorizedByUserId,
                    input.PaymentCounts)),
            cancellationToken);
        return queued;
    }

    public Task MarkClosedAsync(
        PosLocalUserSession session,
        DateTimeOffset closedAt,
        CancellationToken cancellationToken) =>
        identities.MarkWorkSessionClosedAsync(
            session.WorkSessionId, session.UserId, closedAt, cancellationToken);

    private static IReadOnlyList<WorkSessionPaymentTotal> PaymentTotals(
        IReadOnlyList<PosLocalWorkSessionSale> sales,
        decimal otherCash,
        IReadOnlyList<WorkSessionPaymentCount>? counts)
    {
        var amounts = sales.SelectMany(value => value.Payments)
            .GroupBy(value => ClosureMethod(value.MethodCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Amount),
                StringComparer.OrdinalIgnoreCase);
        var creditAmount = sales.Sum(value => value.CreditAmount);
        if (creditAmount > 0) amounts["Credit"] = creditAmount;
        amounts.TryAdd("Cash", 0);
        amounts.TryAdd("Card", 0);
        amounts.TryAdd("Transfer", 0);
        var counted = (counts ?? [])
            .GroupBy(value => value.PaymentMethodCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.CountedAmount),
                StringComparer.OrdinalIgnoreCase);
        return amounts
            .OrderBy(value => PaymentOrder(value.Key))
            .ThenBy(value => value.Key, StringComparer.Ordinal)
            .Select(value =>
            {
                var other = value.Key.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                    ? otherCash
                    : 0;
                var net = value.Value + other;
                var manual = RequiresManualCount(value.Key);
                var hasCount = counted.TryGetValue(value.Key, out var countedAmount);
                return new WorkSessionPaymentTotal(
                    value.Key, value.Value, 0, other, net,
                    manual && hasCount ? countedAmount : null,
                    manual && hasCount ? countedAmount - net : null,
                    manual);
            })
            .ToArray();
    }

    private static int PaymentOrder(string code) => code switch
    {
        "Cash" => 0,
        "DebitCard" => 1,
        "CreditCard" => 2,
        "Card" => 3,
        "Transfer" => 4,
        "Credit" => 5,
        _ => 10
    };

    private static bool RequiresManualCount(string code) =>
        code.Equals("Cash", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("Card", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("Transfer", StringComparison.OrdinalIgnoreCase);

    private static string ClosureMethod(string code) => code switch
    {
        "DebitCard" or "CreditCard" or "Card" => "Card",
        "Transfer" => "Transfer",
        "Cash" => "Cash",
        _ => code
    };
}

public sealed class PosWorkSessionClosureUploader(
    PosOfflineWorkSessionClosureStore store,
    PosEdgeSaleStore sales,
    PosCashMovementStore cashMovements,
    PosWorkSessionClosureServerClient server,
    PosSynchronizationEventLog events)
{
    public async Task<bool> UploadNextAsync(CancellationToken cancellationToken)
    {
        var item = await store.ClaimAsync(cancellationToken);
        if (item is null) return false;
        if (await sales.HasPendingOutboxForWorkSessionAsync(
                item.Value.Value.Closure.WorkSessionId, cancellationToken) ||
            await cashMovements.HasPendingForWorkSessionAsync(
                item.Value.Value.Closure.WorkSessionId, cancellationToken))
        {
            await store.ScheduleRetryAsync(
                item.Value.OperationId,
                item.Value.Attempts,
                "Hay ventas de esta sesión pendientes por subir.",
                cancellationToken);
            events.Record("Info", "WorkSessionClosure", "Cierre espera documentos anteriores",
                item.Value.Value.Closure.WorkSessionId.ToString("D"));
            return false;
        }
        try
        {
            await server.CloseAsync(
                item.Value.Value.Request,
                item.Value.OperationId,
                cancellationToken);
            await store.MarkUploadedAsync(item.Value.OperationId, cancellationToken);
            events.Record("Success", "WorkSessionClosure", "Cierre de caja subido",
                item.Value.Value.Closure.WorkSessionId.ToString("D"));
        }
        catch (Exception exception) when (
            exception is HttpRequestException or PosWorkSessionClosureException)
        {
            if (exception is PosWorkSessionClosureException { StatusCode: 409 })
            {
                var existing = await server.GetClosureAsync(
                    item.Value.Value.Closure.WorkSessionId,
                    item.Value.Value.Request.UserId,
                    cancellationToken);
                if (existing is not null)
                {
                    await store.MarkUploadedAsync(item.Value.OperationId, cancellationToken);
                    events.Record(
                        "Success",
                        "WorkSessionClosure",
                        "Cierre de caja conciliado con el servidor",
                        existing.WorkSessionId.ToString("D"));
                    return true;
                }
            }
            await store.ScheduleRetryAsync(
                item.Value.OperationId,
                item.Value.Attempts,
                exception.Message,
                cancellationToken);
            events.Record("Warning", "WorkSessionClosure", "Cierre de caja pendiente",
                $"{item.Value.Value.Closure.WorkSessionId:D} · {exception.Message}");
            return false;
        }
        return true;
    }
}

public sealed class PosWorkSessionClosureStorageInitializer(
    PosOfflineWorkSessionClosureStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
