using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Auraly.Application.DocumentProcessing;
using Auraly.Commerce.Accounting.Application;
using Auraly.Application.Receivables;
using Auraly.Application.Returns;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Dispatching;
using Auraly.Infrastructure.Dispatching;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public sealed class DispatchSettlementCoordinator
{
    private static readonly TimeSpan MinimumLeaseDelay = TimeSpan.FromMilliseconds(50);
    private readonly Channel<bool> signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public DispatchSettlementCoordinator() => Signal();
    public void Signal() => signals.Writer.TryWrite(true);
    public ValueTask<bool> WaitAsync(CancellationToken token) => signals.Reader.ReadAsync(token);
    public async Task WaitAsync(TimeSpan? retryAfter,TimeProvider timeProvider,CancellationToken token)
    {
        if(retryAfter is null) { await WaitAsync(token); return; }
        // A zero/sub-millisecond deadline can complete immediately and spin SQL
        // claims just before a lease expires. Signals and shutdown still wake it.
        var delay=retryAfter.Value<MinimumLeaseDelay?MinimumLeaseDelay:retryAfter.Value;
        using var deadline=new CancellationTokenSource(delay,timeProvider);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,deadline.Token);
        try { await signals.Reader.ReadAsync(linked.Token); }
        catch(OperationCanceledException) when(!token.IsCancellationRequested&&deadline.IsCancellationRequested) { }
    }
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
        TimeSpan? retryAfter=null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await coordinator.WaitAsync(retryAfter,timeProvider,stoppingToken);
                retryAfter=null;
                while (true)
                {
                    var claim=await ClaimNextAsync(stoppingToken);
                    if(claim.Work is not { } operation) { retryAfter=claim.RetryAfter; break; }
                    await ProcessAsync(operation, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Dispatch settlement worker loop failed.");
                // The durable work remains pending. A new explicit request or
                // host restart wakes it; a queue failure must not hot-loop SQL.
            }
        }
    }

    private async Task<(Operation? Work,TimeSpan? RetryAfter)> ClaimNextAsync(CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var select = Procedure("dbo.DispatchSettlementOperationClaimGet", connection);
        await using var reader = await select.ExecuteReaderAsync(token);
        if(await reader.ReadAsync(token)) return (new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetGuid(3),reader.GetGuid(4),
            reader.GetDateTimeOffset(5),reader.GetInt32(6),reader.GetGuid(7),reader.GetString(8),reader.GetGuid(9),reader.GetString(10)),null);
        await reader.NextResultAsync(token);
        if(!await reader.ReadAsync(token)||reader.IsDBNull(0))return(null,null);
        // SQL supplies both instants so clock skew cannot delay an expired lease.
        var delay=reader.GetDateTimeOffset(0)-reader.GetDateTimeOffset(1);
        return(null,delay>TimeSpan.Zero?delay:TimeSpan.Zero);
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
                { SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm, DispatchPermissionCodes.Settle });

            foreach (var item in returns)
            {
                var accepted = await returnService.ConfirmAsync(returnIdentity,
                    $"dispatch-settlement:{operation.DispatchId:N}:return:{item.SourceDocumentId:N}",
                    new ConfirmSalesReturnRequest(item.ReturnId, operation.BusinessId, item.WarehouseId,
                        item.SourceDocumentId, operation.RequestedAt, item.ReverseUncollected
                            ? ReturnEconomicResolutions.ReverseUncollected : ReturnEconomicResolutions.CustomerCredit,
                        null, item.NotDelivered ? "Mercancía no entregada en despacho" : "Devolución registrada durante la entrega",
                        item.Lines.Select(line => new ConfirmSalesReturnLineRequest(line.LineNumber, line.Quantity, line.Disposition)).ToArray(),
                        ReasonCode: item.ReasonCode,
                        Notes: $"Liquidación automática del despacho {operation.DispatchNumber}.",
                        ReturnScopeCode: item.NotDelivered ? SalesReturnScopes.FullCancellation : SalesReturnScopes.Partial,
                        ReturnedChargeIds: item.ChargeIds,
                        DispatchId: operation.DispatchId), token);
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
                await receivablesService.ConfirmPaymentAsync(paymentIdentity,
                    $"dispatch-settlement:{operation.DispatchId:N}:payment:{item.SourceDocumentId:N}:{item.PaymentKeyMethod}",
                    new ConfirmCustomerPaymentRequest(item.PaymentId, operation.BusinessId, item.CustomerId,
                        null, operation.RequestedAt, "COP",
                        $"Recaudo del despacho {operation.DispatchNumber}.",
                        [new CustomerPaymentAllocationRequest(item.ReceivableId, item.Payments.Sum(payment => payment.Amount))],
                        item.Payments), token);
            }
            var financialWork = await EnsureCashDifferenceDocumentAsync(operation, token);
            await documentWorker.ProcessOneAsync(financialWork.Signal, token);
            if (financialWork.Replay)
                await scope.ServiceProvider.GetRequiredService<AccountingProcessingCoordinator>().RequestPostingAsync(
                    operation.BusinessId, operation.SettlementId, DispatchAccountingDocumentTypes.CashDifference, token);
            // The existing accounting processor completes the settlement in its
            // financial transaction. A pending posting must not appear closed.

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
        var rows = new List<(Guid Source, Guid Warehouse, bool NotDelivered, string ReasonCode, bool ReverseUncollected, ReturnLine Line)>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            rows.Add((reader.GetGuid(0), reader.GetGuid(6), reader.GetString(1) == "NotDelivered",
                reader.IsDBNull(5) ? throw new InvalidOperationException(
                    "No hay un motivo de devolución de venta activo para esta sede.") : reader.GetString(5),
                reader.GetString(7)=="Dispatch" && reader.GetDecimal(8)<reader.GetDecimal(9),
                new(reader.GetInt32(2), reader.GetDecimal(3), reader.GetString(4))));
        var charges = new Dictionary<Guid,List<Guid>>();
        await reader.NextResultAsync(token);
        while(await reader.ReadAsync(token))
        {
            if(!charges.TryGetValue(reader.GetGuid(0),out var items)) charges.Add(reader.GetGuid(0),items=[]);
            items.Add(reader.GetGuid(1));
        }
        return rows.GroupBy(row => new { row.Source, row.Warehouse, row.NotDelivered, row.ReasonCode, row.ReverseUncollected })
            .Select(group => new ReturnWork(DeterministicGuid($"dispatch:{operation.DispatchId:N}:return:{group.Key.Source:N}"),
                group.Key.Source, group.Key.Warehouse, group.Key.NotDelivered, group.Key.ReasonCode,
                group.Select(row => row.Line).ToArray(),group.Key.ReverseUncollected,
                charges.GetValueOrDefault(group.Key.Source,[]))).ToArray();
    }

    private async Task<IReadOnlyList<PaymentWork>> LoadPaymentsAsync(Operation operation, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = Procedure("dbo.DispatchSettlementPaymentsGet", connection);
        command.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        var values = new List<(Guid Source,Guid Customer,Guid Receivable,string KeyMethod,CustomerPaymentTenderRequest Tender)>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var source = reader.GetGuid(0);
            var method = reader.GetString(3);
            values.Add((source,reader.GetGuid(1),reader.GetGuid(2),reader.GetString(11),
                new CustomerPaymentTenderRequest(method,reader.GetDecimal(4),
                    BankAccountId:reader.IsDBNull(6)?null:reader.GetGuid(6),
                    Reference:reader.IsDBNull(5)?null:reader.GetString(5),
                    PaymentMethodId:reader.IsDBNull(7)?null:reader.GetGuid(7),
                    PaymentMethodVersion:reader.IsDBNull(8)?null:reader.GetInt32(8),
                    CardFranchiseCode:reader.IsDBNull(9)?null:reader.GetString(9),
                    ApprovalNumber:reader.IsDBNull(10)?null:reader.GetString(10))));
        }
        return values.GroupBy(value=>new{value.Source,value.Customer,value.Receivable,value.KeyMethod})
            .Select(group=>new PaymentWork(
                DeterministicGuid($"dispatch:{operation.DispatchId:N}:payment:{group.Key.Source:N}:{group.Key.KeyMethod}"),
                group.Key.Source,group.Key.Customer,group.Key.Receivable,group.Key.KeyMethod,
                group.Select(value=>value.Tender).ToArray())).ToArray();
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

    private async Task<FinancialWork> EnsureCashDifferenceDocumentAsync(
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
            Guid? cashDifferenceForm;
            var collections = new List<DispatchCollectedPayment>();
            await using (var select = Procedure("dbo.DispatchSettlementCashDifferenceGet", connection, transaction))
            {
                select.Parameters.AddWithValue("@SettlementId", operation.SettlementId);
                select.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
                select.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
                await using var reader = await select.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token) || reader.IsDBNull(1) || reader.IsDBNull(2))
                    throw new InvalidOperationException(
                        "La liquidación del despacho aún no está lista para registrar su diferencia de efectivo.");
                expected = reader.GetDecimal(0);
                received = reader.GetDecimal(1);
                occurredAt = TimeZoneInfo.ConvertTime(reader.GetDateTimeOffset(2),TimeZoneInfo.FindSystemTimeZoneById(reader.GetString(5)));
                notes = reader.IsDBNull(3) ? null : reader.GetString(3);
                cashDifferenceForm=reader.IsDBNull(4)?null:reader.GetGuid(4);
                await reader.NextResultAsync(token);
                while (await reader.ReadAsync(token))
                    collections.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetDecimal(4)));
            }

            var difference = decimal.Round(received - expected, 4);
            var movementId = DeterministicGuid(
                $"dispatch:{operation.DispatchId:N}:cash-difference:movement");
            await using (var replay = Procedure("dbo.DocumentProcessingJobByDocumentGet", connection, transaction))
            {
                replay.Parameters.AddWithValue("@DocumentId", operation.SettlementId);
                replay.Parameters.AddWithValue("@DocumentType", DispatchAccountingDocumentTypes.CashDifference);
                if (await replay.ExecuteScalarAsync(token) is Guid existing)
                {
                    await transaction.CommitAsync(token);
                    return new(new DocumentProcessingSignal(existing, operation.BusinessId,
                        operation.SettlementId, DispatchAccountingDocumentTypes.CashDifference), true);
                }
            }

            var payload = new DispatchCashDifferencePayload(
                operation.SettlementId, operation.TenantId, operation.BusinessId,
                operation.DispatchId, operation.DispatchNumber, operation.TransporterName,
                expected, received, difference, occurredAt, notes, collections, cashDifferenceForm);
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
            return new(new DocumentProcessingSignal(movementId, operation.BusinessId,
                operation.SettlementId, DispatchAccountingDocumentTypes.CashDifference), false);
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

    private sealed record FinancialWork(DocumentProcessingSignal Signal, bool Replay);

    private sealed record Operation(Guid Id, Guid BusinessId, Guid DispatchId, Guid WorkSessionId, Guid RequestedBy,
        DateTimeOffset RequestedAt, int Attempts, Guid TenantId,
        string DispatchNumber, Guid SettlementId, string TransporterName);
    private sealed record ReturnLine(int LineNumber, decimal Quantity, string Disposition);
    private sealed record ReturnWork(Guid ReturnId, Guid SourceDocumentId, Guid WarehouseId, bool NotDelivered,
        string ReasonCode, IReadOnlyList<ReturnLine> Lines,bool ReverseUncollected,IReadOnlyList<Guid> ChargeIds);
    private sealed record PaymentWork(Guid PaymentId, Guid SourceDocumentId, Guid CustomerId,
        Guid ReceivableId, string PaymentKeyMethod, IReadOnlyList<CustomerPaymentTenderRequest> Payments);
}
