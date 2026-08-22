using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Receivables;
using Auraly.Application.Returns;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Returns;
using Auraly.Infrastructure.Dispatching;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public sealed class DispatchSettlementCoordinator
{
    private readonly Channel<bool> signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public DispatchSettlementCoordinator() => Signal();
    public void Signal() => signals.Writer.TryWrite(true);
    public ValueTask<bool> WaitAsync(CancellationToken token) => signals.Reader.ReadAsync(token);
}

public sealed class DispatchSettlementHostedService(
    IServiceScopeFactory scopes,
    DispatchingSqlConnectionFactory connections,
    DispatchSettlementCoordinator coordinator,
    ILogger<DispatchSettlementHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await coordinator.WaitAsync(stoppingToken);
                while (await ClaimNextAsync(stoppingToken) is { } operation)
                    await ProcessAsync(operation, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Dispatch settlement worker loop failed.");
                coordinator.Signal();
            }
        }
    }

    private async Task<Operation?> ClaimNextAsync(CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        // READPAST is a queue-consumer lock hint and SQL Server only permits it at
        // READ COMMITTED or REPEATABLE READ. UPDLOCK still serializes the selected row.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            const string selectSql = """
                SELECT TOP(1) operation.DispatchSettlementOperationId,operation.BusinessId,
                       operation.DispatchId,operation.RequestedBy,operation.RequestedAt,
                       operation.Attempts,business.TenantId,dispatch.WarehouseId,dispatch.DispatchNumber
                FROM dbo.DispatchSettlementOperations operation WITH(UPDLOCK,READPAST,READCOMMITTEDLOCK)
                INNER JOIN dbo.Businesses business ON business.BusinessId=operation.BusinessId
                INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=operation.DispatchId
                WHERE operation.Status IN(N'Pending',N'Processing')
                  AND operation.NextAttemptAt<=SYSUTCDATETIME()
                ORDER BY operation.NextAttemptAt,operation.RequestedAt;
                """;
            Operation? value = null;
            await using (var select = new SqlCommand(selectSql, connection, transaction))
            await using (var reader = await select.ExecuteReaderAsync(token))
                if (await reader.ReadAsync(token))
                    value = new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                        reader.GetGuid(3), reader.GetDateTimeOffset(4), reader.GetInt32(5) + 1,
                        reader.GetGuid(6), reader.GetGuid(7), reader.GetString(8));
            if (value is null)
            {
                await transaction.CommitAsync(token);
                return null;
            }
            await using var update = new SqlCommand("""
                UPDATE dbo.DispatchSettlementOperations
                SET Status=N'Processing',Attempts=Attempts+1,NextAttemptAt=DATEADD(MINUTE,5,SYSUTCDATETIME()),LastError=NULL
                WHERE DispatchSettlementOperationId=@Id;
                UPDATE dbo.Dispatches SET Status=N'SettlementProcessing',UpdatedAt=SYSUTCDATETIME()
                WHERE DispatchId=@DispatchId AND Status=N'SettlementAttention';
                """, connection, transaction);
            update.Parameters.AddWithValue("@Id", value.Id);
            update.Parameters.AddWithValue("@DispatchId", value.DispatchId);
            await update.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
            return value;
        }
        catch
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    private async Task ProcessAsync(Operation operation, CancellationToken token)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var returnService = scope.ServiceProvider.GetRequiredService<SalesReturnService>();
            var receivablesService = scope.ServiceProvider.GetRequiredService<ReceivablesService>();
            var documentWorker = scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>();
            var returns = await LoadReturnsAsync(operation, token);
            var returnIdentity = new SalesReturnUserIdentity(operation.RequestedBy, operation.TenantId,
                operation.BusinessId, new HashSet<string>(StringComparer.Ordinal)
                { SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm });

            foreach (var item in returns)
            {
                var accepted = await returnService.ConfirmAsync(returnIdentity,
                    $"dispatch-settlement:{operation.DispatchId:N}:return:{item.SourceDocumentId:N}",
                    new ConfirmSalesReturnRequest(item.ReturnId, operation.BusinessId, operation.WarehouseId,
                        item.SourceDocumentId, operation.RequestedAt, ReturnEconomicResolutions.CustomerCredit,
                        null, item.NotDelivered ? "Mercancía no entregada en despacho" : "Devolución registrada durante la entrega",
                        item.Lines.Select(line => new ConfirmSalesReturnLineRequest(line.LineNumber, line.Quantity, line.Disposition)).ToArray(),
                        ReasonCode: item.ReasonCode,
                        Notes: $"Liquidación automática del despacho {operation.DispatchNumber}."), token);
                await documentWorker.ProcessOneAsync(new DocumentProcessingSignal(
                    accepted.MovementId, operation.BusinessId, accepted.ReturnId,
                    SalesReturnDocumentTypes.SalesReturn), token);
            }

            // Each deterministic document is accepted and immediately executed by the canonical
            // engine. Fiscal and accounting continue through their own durable outbox messages;
            // settlement never polls them and a retry cannot reapply completed intrinsic effects.
            var payments = await LoadPaymentsAsync(operation, token);
            var paymentIdentity = new ReceivablesUserIdentity(operation.RequestedBy, operation.TenantId,
                operation.BusinessId, new HashSet<string>(StringComparer.Ordinal)
                { ReceivablesPermissionCodes.RegisterPayment });
            foreach (var item in payments)
            {
                var accepted = await receivablesService.ConfirmPaymentAsync(paymentIdentity,
                    $"dispatch-settlement:{operation.DispatchId:N}:payment:{item.SourceDocumentId:N}:{item.PaymentMethod}",
                    new ConfirmCustomerPaymentRequest(item.PaymentId, operation.BusinessId, item.CustomerId,
                        null, operation.RequestedAt, "COP",
                        item.PaymentMethod == "Deposit" ? CustomerPaymentMethods.BankTransfer : CustomerPaymentMethods.Cash,
                        item.Reference, $"Recaudo del despacho {operation.DispatchNumber}.",
                        [new CustomerPaymentAllocationRequest(item.ReceivableId, item.Amount)]), token);
                await documentWorker.ProcessOneAsync(new DocumentProcessingSignal(
                    accepted.MovementId, operation.BusinessId, accepted.PaymentId,
                    ReceivablesDocumentTypes.Payment), token);
            }
            await CompleteAsync(operation, token);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Settlement operation {OperationId} failed for dispatch {DispatchId}.", operation.Id, operation.DispatchId);
            var attention = operation.Attempts >= 3;
            await RescheduleAsync(operation, exception.Message, attention, token);
            if (!attention) coordinator.Signal();
        }
    }

    private async Task<IReadOnlyList<ReturnWork>> LoadReturnsAsync(Operation operation, CancellationToken token)
    {
        const string sql = """
            SELECT source.SourceDocumentId,delivery.DeliveryStatus,line.LineNumber,
                   CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN line.Quantity ELSE returned.Quantity END,
                   CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN N'Sellable' ELSE returned.InventoryDisposition END,
                   reason.Code
            FROM dbo.DispatchSourceDocuments source
            INNER JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId
            INNER JOIN dbo.SalesDocumentLines line ON line.DocumentId=source.SourceDocumentId
            LEFT JOIN dbo.DispatchDeliveryReturns returned ON returned.DispatchSourceDocumentId=source.DispatchSourceDocumentId
                AND returned.OriginalLineNumber=line.LineNumber
            OUTER APPLY(SELECT TOP(1) r.Code FROM dbo.BusinessReasons r
              WHERE r.BusinessId=@BusinessId AND r.ReasonType=N'SalesReturn' AND r.IsActive=1
              ORDER BY r.DisplayOrder,r.Name,r.Code) reason
            WHERE source.DispatchId=@DispatchId
              AND (delivery.DeliveryStatus=N'NotDelivered' OR returned.DispatchDeliveryReturnId IS NOT NULL)
            ORDER BY source.SourceDocumentId,line.LineNumber;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        command.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
        var rows = new List<(Guid Source, bool NotDelivered, string ReasonCode, ReturnLine Line)>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            rows.Add((reader.GetGuid(0), reader.GetString(1) == "NotDelivered",
                reader.IsDBNull(5) ? throw new InvalidOperationException(
                    "No active sales return reason is configured for this business.") : reader.GetString(5),
                new(reader.GetInt32(2), reader.GetDecimal(3), reader.GetString(4))));
        return rows.GroupBy(row => new { row.Source, row.NotDelivered, row.ReasonCode })
            .Select(group => new ReturnWork(DeterministicGuid($"dispatch:{operation.DispatchId:N}:return:{group.Key.Source:N}"),
                group.Key.Source, group.Key.NotDelivered, group.Key.ReasonCode,
                group.Select(row => row.Line).ToArray())).ToArray();
    }

    private async Task<IReadOnlyList<PaymentWork>> LoadPaymentsAsync(Operation operation, CancellationToken token)
    {
        const string sql = """
            SELECT source.SourceDocumentId,sale.CustomerId,receivable.ReceivableId,payment.PaymentMethod,
                   SUM(payment.Amount),MAX(payment.Reference)
            FROM dbo.DispatchDeliveryPayments payment
            INNER JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=payment.DispatchSourceDocumentId
            INNER JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId
            INNER JOIN dbo.Receivables receivable ON receivable.SourceDocumentId=source.SourceDocumentId
                AND receivable.BusinessId=@BusinessId AND receivable.Status IN(N'Open',N'PartiallyPaid')
            WHERE payment.DispatchId=@DispatchId AND payment.ApplicationType IN(N'InvoicePayment',N'CreditAdvance')
              AND payment.PaymentMethod IN(N'Cash',N'Deposit') AND sale.CustomerId IS NOT NULL
            GROUP BY source.SourceDocumentId,sale.CustomerId,receivable.ReceivableId,payment.PaymentMethod;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        var values = new List<PaymentWork>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var source = reader.GetGuid(0);
            var method = reader.GetString(3);
            values.Add(new(DeterministicGuid($"dispatch:{operation.DispatchId:N}:payment:{source:N}:{method}"),
                source, reader.GetGuid(1), reader.GetGuid(2), method, reader.GetDecimal(4), reader.IsDBNull(5) ? operation.DispatchNumber : reader.GetString(5)));
        }
        return values;
    }

    private async Task RescheduleAsync(Operation operation, string? error, bool attention, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SET XACT_ABORT ON; BEGIN TRAN;
            UPDATE dbo.DispatchSettlementOperations
            SET Status=@OperationStatus,NextAttemptAt=SYSUTCDATETIME(),LastError=@Error
            WHERE DispatchSettlementOperationId=@Id AND Status=N'Processing';
            UPDATE dbo.Dispatches SET Status=@DispatchStatus,UpdatedAt=SYSUTCDATETIME() WHERE DispatchId=@DispatchId;
            UPDATE dbo.DispatchSettlements SET Status=@SettlementStatus WHERE DispatchId=@DispatchId;
            COMMIT;
            """, connection);
        command.Parameters.AddWithValue("@OperationStatus", attention ? "NeedsAttention" : "Pending");
        command.Parameters.AddWithValue("@DispatchStatus", attention ? "SettlementAttention" : "SettlementProcessing");
        command.Parameters.AddWithValue("@SettlementStatus", attention ? "Attention" : "Processing");
        command.Parameters.AddWithValue("@Error", (object?)error?[..Math.Min(error.Length, 2000)] ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", operation.Id);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task CompleteAsync(Operation operation, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SET XACT_ABORT ON; BEGIN TRAN;
            IF EXISTS(SELECT 1 FROM dbo.DispatchSettlementOperations WHERE DispatchSettlementOperationId=@Id AND Status=N'Completed') BEGIN COMMIT; RETURN; END;
            IF EXISTS(
                SELECT 1
                FROM dbo.DocumentProcessingJobs job
                INNER JOIN (
                    SELECT [ReturnId] AS [DocumentId], N'SalesReturn' AS [DocumentType]
                    FROM dbo.SalesReturns
                    WHERE IdempotencyKey LIKE @SettlementKey
                    UNION ALL
                    SELECT [PaymentId], N'ReceivablePayment'
                    FROM dbo.CustomerPayments
                    WHERE IdempotencyKey LIKE @SettlementKey
                ) settlementDocument
                  ON settlementDocument.DocumentId=job.DocumentId
                 AND settlementDocument.DocumentType=job.DocumentType
                WHERE job.Status<>N'Completed'
            ) THROW 51000,'Settlement documents are not fully processed.',1;
            UPDATE dbo.DispatchSettlementOperations SET Status=N'Completed',CompletedAt=SYSUTCDATETIME(),LastError=NULL WHERE DispatchSettlementOperationId=@Id;
            UPDATE dbo.DispatchSettlements SET Status=N'Completed' WHERE DispatchId=@DispatchId;
            UPDATE dbo.Dispatches SET Status=N'Closed',UpdatedBy=@UserId,UpdatedAt=SYSUTCDATETIME() WHERE DispatchId=@DispatchId;
            COMMIT;
            """, connection);
        command.Parameters.AddWithValue("@Id", operation.Id);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        command.Parameters.AddWithValue("@UserId", operation.RequestedBy);
        command.Parameters.AddWithValue("@SettlementKey", $"dispatch-settlement:{operation.DispatchId:N}:%");
        await command.ExecuteNonQueryAsync(token);
    }

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed record Operation(Guid Id, Guid BusinessId, Guid DispatchId, Guid RequestedBy,
        DateTimeOffset RequestedAt, int Attempts, Guid TenantId, Guid WarehouseId, string DispatchNumber);
    private sealed record ReturnLine(int LineNumber, decimal Quantity, string Disposition);
    private sealed record ReturnWork(Guid ReturnId, Guid SourceDocumentId, bool NotDelivered,
        string ReasonCode, IReadOnlyList<ReturnLine> Lines);
    private sealed record PaymentWork(Guid PaymentId, Guid SourceDocumentId, Guid CustomerId,
        Guid ReceivableId, string PaymentMethod, decimal Amount, string Reference);
}
