using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Infrastructure;
using Auraly.Pos.Printing;
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

public sealed record PosLocalWorkSessionRefund(
    Guid ReturnId,
    Guid WorkSessionId,
    string PaymentMethodCode,
    decimal Amount);
public sealed record PosLocalPortfolioTender(string MethodCode, decimal Amount);
public sealed record PosLocalPortfolioPayment(Guid PaymentId, Guid WorkSessionId,
    string Direction, string DocumentNumber, DateTimeOffset PaidAt,
    IReadOnlyList<PosLocalPortfolioTender> Tenders);

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
            CREATE TABLE IF NOT EXISTS PosWorkSessionRefunds(
              ReturnId TEXT NOT NULL PRIMARY KEY,
              WorkSessionId TEXT NOT NULL,
              PaymentMethodCode TEXT NOT NULL,
              Amount TEXT NOT NULL,
              CreatedAt TEXT NOT NULL,
              Accepted INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS IX_PosWorkSessionRefunds_WorkSession
              ON PosWorkSessionRefunds(WorkSessionId);
            CREATE TABLE IF NOT EXISTS PosWorkSessionPortfolioPayments(
              PaymentId TEXT NOT NULL PRIMARY KEY,
              WorkSessionId TEXT NOT NULL,
              Direction TEXT NOT NULL CHECK(Direction IN ('Receivable','Payable')),
              DocumentNumber TEXT NOT NULL,
              PaidAt TEXT NOT NULL,
              TendersJson TEXT NOT NULL,
              Accepted INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS IX_PosWorkSessionPortfolioPayments_WorkSession
              ON PosWorkSessionPortfolioPayments(WorkSessionId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AddAcceptedColumnIfMissingAsync(connection, "PosWorkSessionRefunds", cancellationToken);
        await AddAcceptedColumnIfMissingAsync(connection, "PosWorkSessionPortfolioPayments", cancellationToken);
    }

    private static async Task AddAcceptedColumnIfMissingAsync(SqliteConnection connection,
        string table, CancellationToken cancellationToken)
    {
        await using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (reader.GetString(1) == "Accepted") return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        // Rows written before the provisional state existed were already confirmed.
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN Accepted INTEGER NOT NULL DEFAULT 1;";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> PrepareRefundAsync(PosLocalWorkSessionRefund value,
        CancellationToken cancellationToken = default) =>
        await WriteRefundAsync(value, false, cancellationToken);

    public async Task RecordRefundAsync(
        PosLocalWorkSessionRefund value,
        CancellationToken cancellationToken = default)
    {
        await WriteRefundAsync(value, true, cancellationToken);
    }

    private async Task<bool> WriteRefundAsync(PosLocalWorkSessionRefund value,
        bool accepted, CancellationToken cancellationToken)
    {
        if (value.ReturnId == Guid.Empty || value.WorkSessionId == Guid.Empty ||
            value.Amount < 0 || string.IsNullOrWhiteSpace(value.PaymentMethodCode))
            throw new ArgumentException("La devolución confirmada no es válida.", nameof(value));
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = accepted ? """
            INSERT INTO PosWorkSessionRefunds(
              ReturnId,WorkSessionId,PaymentMethodCode,Amount,CreatedAt,Accepted)
            VALUES($return,$session,$method,$amount,$now,1)
            ON CONFLICT(ReturnId) DO UPDATE SET
              WorkSessionId=excluded.WorkSessionId,
              PaymentMethodCode=excluded.PaymentMethodCode,
              Amount=excluded.Amount,Accepted=1;
            """ : """
            INSERT INTO PosWorkSessionRefunds(
              ReturnId,WorkSessionId,PaymentMethodCode,Amount,CreatedAt,Accepted)
            VALUES($return,$session,$method,$amount,$now,0)
            ON CONFLICT(ReturnId) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$return", value.ReturnId.ToString("D"));
        command.Parameters.AddWithValue("$session", value.WorkSessionId.ToString("D"));
        command.Parameters.AddWithValue("$method", value.PaymentMethodCode);
        command.Parameters.AddWithValue(
            "$amount", value.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        var provisional = !accepted && inserted == 0 &&
            await ExistingProvisionalAsync(connection, transaction, "PosWorkSessionRefunds",
                "ReturnId", value.ReturnId, value.WorkSessionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inserted == 1 || provisional;
    }

    public async Task<IReadOnlyList<PosLocalWorkSessionRefund>> ReadRefundsAsync(
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ReturnId,PaymentMethodCode,Amount
            FROM PosWorkSessionRefunds
            WHERE WorkSessionId=$session AND Accepted=1 AND CAST(Amount AS REAL)>0
            ORDER BY CreatedAt,ReturnId;
            """;
        command.Parameters.AddWithValue("$session", workSessionId.ToString("D"));
        var values = new List<PosLocalWorkSessionRefund>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new PosLocalWorkSessionRefund(
                Guid.Parse(reader.GetString(0)), workSessionId, reader.GetString(1),
                decimal.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture)));
        return values;
    }

    public async Task RemoveRefundAsync(Guid returnId, Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM PosWorkSessionRefunds
            WHERE ReturnId=$id AND WorkSessionId=$session AND Accepted=0;
            """;
        command.Parameters.AddWithValue("$id", returnId.ToString("D"));
        command.Parameters.AddWithValue("$session", workSessionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> PreparePortfolioPaymentAsync(PosLocalPortfolioPayment value,
        CancellationToken cancellationToken = default) =>
        await WritePortfolioPaymentAsync(value, false, cancellationToken);

    public async Task RecordPortfolioPaymentAsync(PosLocalPortfolioPayment value,
        CancellationToken cancellationToken = default)
    {
        await WritePortfolioPaymentAsync(value, true, cancellationToken);
    }

    private async Task<bool> WritePortfolioPaymentAsync(PosLocalPortfolioPayment value,
        bool accepted,
        CancellationToken cancellationToken = default)
    {
        if(value.PaymentId==Guid.Empty||value.WorkSessionId==Guid.Empty||
           value.Direction is not ("Receivable" or "Payable")||
           string.IsNullOrWhiteSpace(value.DocumentNumber)||value.Tenders.Count==0||
           value.Tenders.Any(tender=>string.IsNullOrWhiteSpace(tender.MethodCode)||tender.Amount<=0))
            throw new ArgumentException("El pago de cartera confirmado no es válido.",nameof(value));
        await using var connection=new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction=connection.BeginTransaction(IsolationLevel.Serializable);
        await using var command=connection.CreateCommand();
        command.Transaction=transaction;
        command.CommandText=accepted ? """
            INSERT INTO PosWorkSessionPortfolioPayments
              (PaymentId,WorkSessionId,Direction,DocumentNumber,PaidAt,TendersJson,Accepted)
            VALUES($id,$session,$direction,$number,$paidAt,$tenders,1)
            ON CONFLICT(PaymentId) DO UPDATE SET
              WorkSessionId=excluded.WorkSessionId,Direction=excluded.Direction,
              DocumentNumber=excluded.DocumentNumber,PaidAt=excluded.PaidAt,
              TendersJson=excluded.TendersJson,Accepted=1;
            """ : """
            INSERT INTO PosWorkSessionPortfolioPayments
              (PaymentId,WorkSessionId,Direction,DocumentNumber,PaidAt,TendersJson,Accepted)
            VALUES($id,$session,$direction,$number,$paidAt,$tenders,0)
            ON CONFLICT(PaymentId) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id",value.PaymentId.ToString("D"));
        command.Parameters.AddWithValue("$session",value.WorkSessionId.ToString("D"));
        command.Parameters.AddWithValue("$direction",value.Direction);
        command.Parameters.AddWithValue("$number",value.DocumentNumber);
        command.Parameters.AddWithValue("$paidAt",value.PaidAt.ToString("O"));
        command.Parameters.AddWithValue("$tenders",JsonSerializer.Serialize(value.Tenders,Json));
        var inserted=await command.ExecuteNonQueryAsync(cancellationToken);
        var provisional=!accepted&&inserted==0&&
            await ExistingProvisionalAsync(connection,transaction,"PosWorkSessionPortfolioPayments",
                "PaymentId",value.PaymentId,value.WorkSessionId,cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inserted==1||provisional;
    }

    private static async Task<bool> ExistingProvisionalAsync(SqliteConnection connection,
        SqliteTransaction transaction, string table, string keyColumn, Guid id,
        Guid workSessionId, CancellationToken cancellationToken)
    {
        await using var command=connection.CreateCommand();
        command.Transaction=transaction;
        command.CommandText=$"SELECT Accepted FROM {table} WHERE {keyColumn}=$id AND WorkSessionId=$session;";
        command.Parameters.AddWithValue("$id",id.ToString("D"));
        command.Parameters.AddWithValue("$session",workSessionId.ToString("D"));
        return await command.ExecuteScalarAsync(cancellationToken) is 0L;
    }

    public async Task<IReadOnlyList<PosLocalPortfolioPayment>> ReadPortfolioPaymentsAsync(
        Guid workSessionId,CancellationToken cancellationToken = default)
    {
        await using var connection=new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT PaymentId,Direction,DocumentNumber,PaidAt,TendersJson
            FROM PosWorkSessionPortfolioPayments WHERE WorkSessionId=$session AND Accepted=1
            ORDER BY PaidAt,PaymentId;
            """;
        command.Parameters.AddWithValue("$session",workSessionId.ToString("D"));
        var values=new List<PosLocalPortfolioPayment>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
            values.Add(new(Guid.Parse(reader.GetString(0)),workSessionId,reader.GetString(1),
                reader.GetString(2),DateTimeOffset.Parse(reader.GetString(3)),
                JsonSerializer.Deserialize<List<PosLocalPortfolioTender>>(reader.GetString(4),Json)??[]));
        return values;
    }

    public async Task RemovePortfolioPaymentAsync(Guid paymentId, Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM PosWorkSessionPortfolioPayments
            WHERE PaymentId=$id AND WorkSessionId=$session AND Accepted=0;
            """;
        command.Parameters.AddWithValue("$id", paymentId.ToString("D"));
        command.Parameters.AddWithValue("$session", workSessionId.ToString("D"));
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
        read.CommandText = $$"""
            SELECT DocumentId,Payload,AttemptCount
            FROM Outbox current
            WHERE current.Type=$type AND ((current.Status IN ('Pending','RetryScheduled')
                   AND (current.NextAttemptAt IS NULL OR current.NextAttemptAt<=$now))
               OR (current.Status='Uploading' AND current.LastAttemptAt<$stale))
              AND {{PosOutboxOrdering.NoBlockingPriorRowSql}}
            ORDER BY current.LocalSequence LIMIT 1;
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
    PosLocalWorkSessionStore workSessions,
    PosOfflineWorkSessionClosureStore store,
    PosEdgeRuntimeContext runtime,
    PosWorkstationIdentity workstation,
    TimeProvider timeProvider)
{
    public async Task<bool> HasPendingLocalDocumentsAsync(
        Guid workSessionId, CancellationToken cancellationToken) =>
        await sales.HasPendingOutboxForWorkSessionAsync(workSessionId, cancellationToken) ||
        await cashMovements.HasPendingForWorkSessionAsync(workSessionId, cancellationToken);

    public async Task<WorkSessionClosurePreviewView> PreviewAsync(
        PosLocalUserSession session,
        CancellationToken cancellationToken)
    {
        var localSales = await sales.ReadWorkSessionSalesAsync(
            session.WorkSessionId, cancellationToken);
        var cashMovementDetails = await cashMovements.ReadWorkSessionDetailsAsync(
            session.WorkSessionId, session.DisplayName, cancellationToken);
        var refunds = await store.ReadRefundsAsync(
            session.WorkSessionId, cancellationToken);
        var portfolio = await store.ReadPortfolioPaymentsAsync(
            session.WorkSessionId, cancellationToken);
        var openedAt = await identities.WorkSessionOpenedAtAsync(
            session.WorkSessionId, cancellationToken);
        var lastActivity = localSales.Select(value=>value.IssuedAt)
            .Concat(portfolio.Select(value=>value.PaidAt))
            .DefaultIfEmpty(openedAt).Max();
        var totals = PaymentTotals(localSales, refunds, cashMovementDetails, portfolio, null);
        var netCashMovements = cashMovementDetails.Sum(movement =>
            movement.Direction == CashMovementDirections.In
                ? movement.Amount
                : -movement.Amount);
        var netPortfolio = portfolio.Sum(payment => payment.Tenders.Sum(tender =>
            payment.Direction == "Receivable" ? tender.Amount : -tender.Amount));
        var creditSales = localSales
            .Where(value => value.CreditAmount > 0)
            .Select(value => new WorkSessionCreditSale(
                value.CustomerName,
                value.DocumentNumber,
                value.CreditAmount))
            .ToArray();
        var receivablePayments = PortfolioDetails(portfolio, "Receivable");
        var payablePayments = PortfolioDetails(portfolio, "Payable");
        return new WorkSessionClosurePreviewView(
            session.WorkSessionId,
            runtime.BusinessId.Value,
            workstation.BusinessName,
            null,
            null,
            session.UserId,
            session.DisplayName,
            openedAt,
            lastActivity,
            localSales.Sum(value => value.Total),
            refunds.Sum(value => value.Amount),
            netCashMovements + netPortfolio,
            localSales.Sum(value => value.Total) - refunds.Sum(value => value.Amount) + netCashMovements + netPortfolio,
            totals.Single(value => value.PaymentMethodCode == "Cash").NetAmount,
            totals,
            localSales.Count,
            localSales.Count(value => value.CreditAmount > 0),
            localSales.Sum(value => value.CreditAmount),
            refunds.Count,
            creditSales,
            cashMovementDetails,
            Auraly.Application.Sales.InvoiceChargeClosureProjection.MapPayments(
                localSales.SelectMany(sale => sale.InvoiceCharges ?? []), ClosureMethod),
            receivablePayments,
            payablePayments);
    }

    public async Task<WorkSessionClosureView> CloseAsync(
        PosLocalUserSession session,
        CloseLocalWorkSessionRequest input,
        Guid authorizedByUserId,
        bool authorizedToCloseWithPausedSales,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(session, cancellationToken);
        var closedAt = timeProvider.GetUtcNow();
        var totals = ApplyCounts(preview.PaymentTotals, input.PaymentCounts);
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
            preview.ReturnCount,
            preview.CreditSales,
            PosPrintTemplateCatalog.WorkSessionClosure.Version,
            preview.CashMovements, preview.InvoiceCharges,
            preview.ReceivablePayments, preview.PayablePayments);
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
                    input.PaymentCounts,
                    authorizedToCloseWithPausedSales)),
            cancellationToken);
        return queued;
    }

    public Task MarkClosedAsync(
        PosLocalUserSession session,
        DateTimeOffset closedAt,
        CancellationToken cancellationToken) =>
        MarkClosedCoreAsync(session, closedAt, cancellationToken);

    private async Task MarkClosedCoreAsync(
        PosLocalUserSession session,
        DateTimeOffset closedAt,
        CancellationToken cancellationToken)
    {
        await workSessions.MarkClosedAsync(
            session.WorkSessionId, session.UserId, closedAt, cancellationToken);
    }

    private static IReadOnlyList<WorkSessionPortfolioPayment> PortfolioDetails(
        IReadOnlyList<PosLocalPortfolioPayment> payments, string direction) =>
        payments.Where(payment => payment.Direction == direction)
            .Select(payment => new WorkSessionPortfolioPayment(
                payment.PaymentId, payment.DocumentNumber, string.Empty,
                payment.Tenders.Sum(tender => tender.Amount), payment.PaidAt, []))
            .ToArray();

    private static IReadOnlyList<WorkSessionPaymentTotal> PaymentTotals(
        IReadOnlyList<PosLocalWorkSessionSale> sales,
        IReadOnlyList<PosLocalWorkSessionRefund> refunds,
        IReadOnlyList<WorkSessionCashMovementDetail> cashMovements,
        IReadOnlyList<PosLocalPortfolioPayment> portfolio,
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
        var refundAmounts = refunds
            .Where(value => value.PaymentMethodCode != "CustomerCredit")
            .GroupBy(value => ClosureMethod(value.PaymentMethodCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Amount),
                StringComparer.OrdinalIgnoreCase);
        foreach (var method in refundAmounts.Keys) amounts.TryAdd(method, 0);
        var portfolioAmounts=portfolio.SelectMany(payment=>payment.Tenders.Select(tender=>
                (Method:ClosureMethod(tender.MethodCode),Direction:payment.Direction,Amount:tender.Amount)))
            .GroupBy(item=>item.Method,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group=>group.Key,group=>(Receivable:group.Where(item=>item.Direction=="Receivable").Sum(item=>item.Amount),
                Payable:group.Where(item=>item.Direction=="Payable").Sum(item=>item.Amount)),StringComparer.OrdinalIgnoreCase);
        foreach(var method in portfolioAmounts.Keys)amounts.TryAdd(method,0);
        var counted = (counts ?? [])
            .GroupBy(value => value.PaymentMethodCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.CountedAmount),
                StringComparer.OrdinalIgnoreCase);
        return amounts
            .OrderBy(value => PaymentOrder(value.Key))
            .ThenBy(value => value.Key, StringComparer.Ordinal)
            .Select(value =>
            {
                var portfolioTotal = portfolioAmounts.GetValueOrDefault(value.Key);
                var other = portfolioTotal.Receivable - portfolioTotal.Payable +
                    (value.Key.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                    ? cashMovements.Sum(movement =>
                        movement.Direction == CashMovementDirections.In
                            ? movement.Amount
                            : -movement.Amount)
                    : 0);
                var refund = refundAmounts.GetValueOrDefault(value.Key);
                var net = value.Value - refund + other;
                var manual = RequiresManualCount(value.Key);
                var hasCount = counted.TryGetValue(value.Key, out var countedAmount);
                return new WorkSessionPaymentTotal(
                    value.Key, value.Value, refund, other, net,
                    manual && hasCount ? countedAmount : null,
                    manual && hasCount ? countedAmount - net : null,
                    manual,
                    value.Key.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? cashMovements.Where(movement =>
                            movement.Direction == CashMovementDirections.In)
                            .Sum(movement => movement.Amount)
                        : 0,
                    value.Key.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? cashMovements.Where(movement =>
                            movement.Direction == CashMovementDirections.Out)
                            .Sum(movement => movement.Amount)
                        : 0,
                    portfolioTotal.Receivable,
                    portfolioTotal.Payable);
            })
            .ToArray();
    }

    private static IReadOnlyList<WorkSessionPaymentTotal> ApplyCounts(
        IReadOnlyList<WorkSessionPaymentTotal> totals,
        IReadOnlyList<WorkSessionPaymentCount> counts)
    {
        var counted = counts
            .GroupBy(value => value.PaymentMethodCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.CountedAmount),
                StringComparer.OrdinalIgnoreCase);
        return totals.Select(total =>
        {
            var hasCount = counted.TryGetValue(
                total.PaymentMethodCode, out var countedAmount);
            return total with
            {
                CountedAmount = total.RequiresCount && hasCount ? countedAmount : null,
                Difference = total.RequiresCount && hasCount
                    ? countedAmount - total.NetAmount
                    : null
            };
        }).ToArray();
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
    PosOfflineWorkSessionClosureService offline,
    PosWorkSessionClosureServerClient server,
    PosSynchronizationEventLog events)
{
    public async Task<bool> UploadNextAsync(CancellationToken cancellationToken)
    {
        var item = await store.ClaimAsync(cancellationToken);
        if (item is null) return false;
        if (await offline.HasPendingLocalDocumentsAsync(
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
                $"{item.Value.Value.Closure.WorkSessionId:D} · No fue posible sincronizarlo; se reintentará automáticamente.");
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
