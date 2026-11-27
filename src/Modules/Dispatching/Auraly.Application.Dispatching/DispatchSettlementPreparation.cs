using Auraly.Application.DocumentProcessing;
using Auraly.Application.Receivables;
using Auraly.Application.Returns;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Returns;
using Auraly.Commerce.Accounting.Application;

namespace Auraly.Application.Dispatching;

public enum DispatchPreparationPhase : byte { Returns, Payments, CashDifference, Submitted }

public sealed record DispatchSettlementWork(
    Guid Id, Guid BusinessId, Guid DispatchId, Guid WorkSessionId, Guid RequestedBy,
    DateTimeOffset RequestedAt, int Attempts, Guid TenantId, string DispatchNumber,
    Guid SettlementId, string TransporterName, DispatchPreparationPhase Phase = DispatchPreparationPhase.Returns,
    Guid? LeaseId = null, string? PlanJson = null);

public sealed record DispatchReturnLine(int LineNumber, decimal Quantity, string Disposition);

public sealed record DispatchReturnWork(Guid ReturnId, Guid SourceDocumentId, Guid WarehouseId, bool NotDelivered,
    string ReasonCode, IReadOnlyList<DispatchReturnLine> Lines, bool ReverseUncollected, IReadOnlyList<Guid> ChargeIds);

public sealed record DispatchPaymentWork(Guid PaymentId, Guid SourceDocumentId, Guid CustomerId,
    Guid ReceivableId, string PaymentKeyMethod, IReadOnlyList<CustomerPaymentTenderRequest> Payments);

public sealed record DispatchSettlementPrerequisite(Guid DocumentId, string DocumentType);
public sealed record DispatchFinancialWork(DocumentProcessingSignal Signal, bool Replay);

public interface IDispatchSettlementPreparationStore
{
    Task<DispatchSettlementWork?> ClaimAsync(DispatchSettlementSignal signal, CancellationToken token);
    Task<IReadOnlyList<DispatchReturnWork>> LoadReturnsAsync(DispatchSettlementWork operation, CancellationToken token);
    Task<IReadOnlyList<DispatchPaymentWork>> LoadPaymentsAsync(DispatchSettlementWork operation, CancellationToken token);
    Task<DispatchFinancialWork> EnsureCashDifferenceDocumentAsync(DispatchSettlementWork operation, CancellationToken token);
    Task AdvanceAsync(DispatchSettlementWork operation, IReadOnlyList<DispatchSettlementPrerequisite> prerequisites,
        CancellationToken token);
    Task RescheduleAsync(DispatchSettlementWork operation, string error, CancellationToken token);
}

public sealed class DispatchSettlementPreparation(
    IDispatchSettlementPreparationStore store,
    SalesReturnService returns,
    ReceivablesService receivables,
    AccountingProcessingCoordinator accounting,
    IDocumentProcessingSignalPublisher documents)
{
    public async Task ProcessAsync(DispatchSettlementSignal signal, CancellationToken token)
    {
        var operation = await store.ClaimAsync(signal, token);
        if (operation is null)
            return;
        try
        {
            IReadOnlyList<DispatchSettlementPrerequisite> prerequisites = [];
            switch (operation.Phase)
            {
                case DispatchPreparationPhase.Returns:
                    var returnPlan = await store.LoadReturnsAsync(operation, token);
                    foreach (var item in returnPlan)
                        await AcceptReturnAsync(operation, item, token);
                    prerequisites = returnPlan.Select(item =>
                        new DispatchSettlementPrerequisite(item.ReturnId, SalesReturnDocumentTypes.SalesReturn)).ToArray();
                    break;
                case DispatchPreparationPhase.Payments:
                    var paymentPlan = await store.LoadPaymentsAsync(operation, token);
                    foreach (var item in paymentPlan)
                    {
                        var accepted = await AcceptPaymentAsync(operation, item, token);
                        if (accepted.IdempotentReplay)
                            await accounting.RequestPostingAsync(operation.BusinessId, item.PaymentId,
                                ReceivablesDocumentTypes.Payment, token);
                    }
                    prerequisites = paymentPlan.Select(item =>
                        new DispatchSettlementPrerequisite(item.PaymentId, ReceivablesDocumentTypes.Payment)).ToArray();
                    break;
                case DispatchPreparationPhase.CashDifference:
                    var work = await store.EnsureCashDifferenceDocumentAsync(operation, token);
                    await documents.PublishAsync(work.Signal, token);
                    break;
                default:
                    throw new InvalidOperationException("La etapa de preparación del despacho no es válida.");
            }
            await store.AdvanceAsync(operation, prerequisites, token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            const string fallback = "No fue posible completar la preparación de la conciliación.";
            var message = error is SalesReturnValidationException or SalesReturnConflictException or ReceivablesValidationException
                or ReceivablesConflictException ? error.Message : fallback;
            await store.RescheduleAsync(operation, message, token);
            throw;
        }
    }

    public Task<SalesReturnAcceptance> AcceptReturnAsync(DispatchSettlementWork operation, DispatchReturnWork item,
        CancellationToken token)
    {
        var actor = new SalesReturnUserIdentity(operation.RequestedBy, operation.TenantId, operation.BusinessId,
            new HashSet<string>(StringComparer.Ordinal)
            { SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm, DispatchPermissionCodes.Settle });
        return returns.ConfirmAsync(actor, $"dispatch-settlement:{operation.DispatchId:N}:return:{item.SourceDocumentId:N}",
            new ConfirmSalesReturnRequest(item.ReturnId, operation.BusinessId, item.WarehouseId,
                item.SourceDocumentId, operation.RequestedAt, item.ReverseUncollected
                    ? ReturnEconomicResolutions.ReverseUncollected : ReturnEconomicResolutions.CustomerCredit,
                null, item.NotDelivered ? "Mercancía no entregada en despacho" : "Devolución registrada durante la entrega",
                item.Lines.Select(line => new ConfirmSalesReturnLineRequest(line.LineNumber, line.Quantity, line.Disposition)).ToArray(),
                ReasonCode: item.ReasonCode, Notes: $"Liquidación automática del despacho {operation.DispatchNumber}.",
                ReturnScopeCode: item.NotDelivered ? SalesReturnScopes.FullCancellation : SalesReturnScopes.Partial,
                ReturnedChargeIds: item.ChargeIds, DispatchId: operation.DispatchId), token);
    }

    public Task<CustomerPaymentAcceptance> AcceptPaymentAsync(DispatchSettlementWork operation, DispatchPaymentWork item,
        CancellationToken token)
    {
        var actor = new ReceivablesUserIdentity(operation.RequestedBy, operation.TenantId, operation.BusinessId,
            new HashSet<string>(StringComparer.Ordinal) { ReceivablesPermissionCodes.RegisterPayment });
        return receivables.ConfirmPaymentAsync(actor,
            $"dispatch-settlement:{operation.DispatchId:N}:payment:{item.SourceDocumentId:N}:{item.PaymentKeyMethod}",
            new ConfirmCustomerPaymentRequest(item.PaymentId, operation.BusinessId, item.CustomerId, null,
                operation.RequestedAt, "COP", $"Recaudo del despacho {operation.DispatchNumber}.",
                [new CustomerPaymentAllocationRequest(item.ReceivableId, item.Payments.Sum(payment => payment.Amount))],
                item.Payments), token);
    }
}
