using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Receivables;
using Auraly.Application.Returns;
using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Application;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Dispatching;
using Auraly.Infrastructure.Dispatching;
using Auraly.Infrastructure.Persistence;
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
    TimeProvider timeProvider,
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
            Operation? value = null;
            await using (var select = Procedure("dbo.DispatchSettlementOperationClaimGet", connection, transaction))
            await using (var reader = await select.ExecuteReaderAsync(token))
                if (await reader.ReadAsync(token))
                    value = new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                        reader.GetGuid(3), reader.GetDateTimeOffset(4), reader.GetInt32(5) + 1,
                        reader.GetGuid(6), reader.GetGuid(7), reader.GetString(8),
                        reader.GetGuid(9), reader.GetString(10));
            if (value is null)
            {
                await transaction.CommitAsync(token);
                return null;
            }
            await using var update = Procedure("dbo.DispatchSettlementOperationClaimMark", connection, transaction);
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
                await ActivateDownstreamAsync(scope.ServiceProvider,
                    operation.BusinessId, accepted.ReturnId,
                    SalesReturnDocumentTypes.SalesReturn, token);
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
                await ActivateDownstreamAsync(scope.ServiceProvider,
                    operation.BusinessId, accepted.PaymentId,
                    ReceivablesDocumentTypes.Payment, token);
            }
            if (await EnsureCashDifferenceDocumentAsync(operation, token) is { } differenceSignal)
            {
                await documentWorker.ProcessOneAsync(differenceSignal, token);
                await ActivateDownstreamAsync(scope.ServiceProvider,
                    operation.BusinessId, operation.SettlementId,
                    DispatchAccountingDocumentTypes.CashDifference, token);
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
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = Procedure("dbo.DispatchSettlementReturnsGet", connection);
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
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = Procedure("dbo.DispatchSettlementPaymentsGet", connection);
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
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var command = Procedure("dbo.DispatchSettlementOperationReschedule", connection, transaction);
        command.Parameters.AddWithValue("@OperationStatus", attention ? "NeedsAttention" : "Pending");
        command.Parameters.AddWithValue("@DispatchStatus", attention ? "SettlementAttention" : "SettlementProcessing");
        command.Parameters.AddWithValue("@SettlementStatus", attention ? "Attention" : "Processing");
        command.Parameters.AddWithValue("@Error", (object?)error?[..Math.Min(error.Length, 2000)] ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", operation.Id);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        try
        {
            await command.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task ActivateDownstreamAsync(
        IServiceProvider services,
        Guid businessId,
        Guid documentId,
        string documentType,
        CancellationToken token)
    {
        if (documentType == SalesReturnDocumentTypes.SalesReturn)
            await services.GetRequiredService<FiscalProcessingCoordinator>()
                .RequestGenerationAsync(businessId, documentId, token);
        if (AccountingProcessingPolicy.Supports(documentType))
            await services.GetRequiredService<AccountingProcessingCoordinator>()
                .RequestPostingAsync(businessId, documentId, documentType, token);
        if (SalesReportingProcessingPolicy.Supports(documentType))
            await services.GetRequiredService<SalesReportingProcessingCoordinator>()
                .RequestProjectionAsync(businessId, documentId, documentType, token);
    }

    private async Task<DocumentProcessingSignal?> EnsureCashDifferenceDocumentAsync(
        Operation operation,
        CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, token);
        try
        {
            decimal expected;
            decimal received;
            DateTimeOffset occurredAt;
            string? notes;
            await using (var select = Procedure("dbo.DispatchSettlementCashDifferenceGet", connection, transaction))
            {
                select.Parameters.AddWithValue("@SettlementId", operation.SettlementId);
                select.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
                select.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
                await using var reader = await select.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token) || reader.IsDBNull(1) || reader.IsDBNull(2))
                    throw new InvalidOperationException(
                        "The dispatch settlement is not ready to recognize its cash difference.");
                expected = reader.GetDecimal(0);
                received = reader.GetDecimal(1);
                occurredAt = reader.GetDateTimeOffset(2);
                notes = reader.IsDBNull(3) ? null : reader.GetString(3);
            }

            var difference = decimal.Round(received - expected, 4);
            if (difference == 0)
            {
                await transaction.CommitAsync(token);
                return null;
            }

            var movementId = DeterministicGuid(
                $"dispatch:{operation.DispatchId:N}:cash-difference:movement");
            await using (var replay = Procedure("dbo.DocumentProcessingJobByDocumentGet", connection, transaction))
            {
                replay.Parameters.AddWithValue("@DocumentId", operation.SettlementId);
                replay.Parameters.AddWithValue("@DocumentType", DispatchAccountingDocumentTypes.CashDifference);
                if (await replay.ExecuteScalarAsync(token) is Guid existing)
                {
                    await transaction.CommitAsync(token);
                    return new DocumentProcessingSignal(existing, operation.BusinessId,
                        operation.SettlementId, DispatchAccountingDocumentTypes.CashDifference);
                }
            }

            var payload = new DispatchCashDifferencePayload(
                operation.SettlementId, operation.TenantId, operation.BusinessId,
                operation.DispatchId, operation.DispatchNumber, operation.TransporterName,
                expected, received, difference, occurredAt, notes);
            var json = System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            var now = timeProvider.GetUtcNow();
            var sequence = await SqlOperationalDocumentAllocator.AllocateSequenceAsync(
                connection, transaction, operation.BusinessId, now, token);
            await using (var insert = Procedure("dbo.DispatchCashDifferenceDocumentCreate", connection, transaction))
            {
                insert.Parameters.AddWithValue("@JobId", movementId);
                insert.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
                insert.Parameters.AddWithValue("@Sequence", sequence);
                insert.Parameters.AddWithValue("@DocumentId", operation.SettlementId);
                insert.Parameters.AddWithValue("@DocumentType", DispatchAccountingDocumentTypes.CashDifference);
                insert.Parameters.AddWithValue("@Now", now);
                insert.Parameters.AddWithValue("@Payload", json);
                insert.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = hash;
                await insert.ExecuteNonQueryAsync(token);
            }
            await transaction.CommitAsync(token);
            return new DocumentProcessingSignal(movementId, operation.BusinessId,
                operation.SettlementId, DispatchAccountingDocumentTypes.CashDifference);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task CompleteAsync(Operation operation, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var command = Procedure("dbo.DispatchSettlementOperationComplete", connection, transaction);
        command.Parameters.AddWithValue("@Id", operation.Id);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        command.Parameters.AddWithValue("@UserId", operation.RequestedBy);
        command.Parameters.AddWithValue("@SettlementKey", $"dispatch-settlement:{operation.DispatchId:N}:%");
        try
        {
            await command.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static SqlCommand Procedure(
        string name,
        SqlConnection connection,
        SqlTransaction? transaction = null) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure };

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed record Operation(Guid Id, Guid BusinessId, Guid DispatchId, Guid RequestedBy,
        DateTimeOffset RequestedAt, int Attempts, Guid TenantId, Guid WarehouseId,
        string DispatchNumber, Guid SettlementId, string TransporterName);
    private sealed record ReturnLine(int LineNumber, decimal Quantity, string Disposition);
    private sealed record ReturnWork(Guid ReturnId, Guid SourceDocumentId, bool NotDelivered,
        string ReasonCode, IReadOnlyList<ReturnLine> Lines);
    private sealed record PaymentWork(Guid PaymentId, Guid SourceDocumentId, Guid CustomerId,
        Guid ReceivableId, string PaymentMethod, decimal Amount, string Reference);
}
