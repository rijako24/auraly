using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Expenses;

namespace Auraly.Application.Expenses;

public interface IExpenseStore
{
    Task<ExpenseAcceptance?> FindReplayAsync(ExpenseUserIdentity user, string idempotencyKey,
        ConfirmExpenseRequest request, ExpenseAmounts amounts, CancellationToken ct);
    Task<ExpenseConceptView?> GetConceptAsync(ExpenseUserIdentity user, Guid conceptId, CancellationToken ct);
    Task<ExpenseWorkspaceOptions> GetOptionsAsync(ExpenseUserIdentity user, CancellationToken ct);
    Task<IReadOnlyList<ExpenseConceptView>> ListConceptsAsync(ExpenseUserIdentity user, bool includeInactive, CancellationToken ct);
    Task<ExpenseConceptView> SaveConceptAsync(ExpenseUserIdentity user, SaveExpenseConceptRequest request, CancellationToken ct);
    Task<ExpensePage> ListAsync(ExpenseUserIdentity user, int page, int pageSize, string? search, Guid? conceptId,
        Guid? supplierId, DateOnly? from, DateOnly? to, string? status, string? payableStatus, CancellationToken ct);
    Task<ExpenseDetail?> GetAsync(ExpenseUserIdentity user, Guid expenseId, CancellationToken ct);
    Task<ExpenseCancellationAcceptance> CancelAsync(ExpenseUserIdentity user, Guid expenseId,
        CancelExpenseRequest request, CancellationToken ct);
    Task<ExpenseAcceptance> AcceptAsync(ExpenseUserIdentity user, string idempotencyKey,
        ConfirmExpenseRequest request, ExpenseAmounts amounts, WithholdingCalculationSnapshot withholding, CancellationToken ct);
}

public sealed class ExpenseService(IExpenseStore store, WithholdingService withholding,
    IAccountingProcessingSignalPublisher signals, Auraly.Application.Fiscal.FiscalProcessingCoordinator fiscal)
{
    public Task<ExpenseWorkspaceOptions> GetOptionsAsync(ExpenseUserIdentity user, CancellationToken ct = default)
    { Demand(user, ExpensePermissionCodes.Read); return store.GetOptionsAsync(user, ct); }

    public Task<IReadOnlyList<ExpenseConceptView>> ListConceptsAsync(ExpenseUserIdentity user, bool includeInactive,
        CancellationToken ct = default)
    { Demand(user, ExpensePermissionCodes.Read); return store.ListConceptsAsync(user, includeInactive, ct); }

    public Task<ExpenseConceptView> SaveConceptAsync(ExpenseUserIdentity user, SaveExpenseConceptRequest request,
        CancellationToken ct = default)
    {
        Demand(user, ExpensePermissionCodes.Configure);
        if (request.BusinessId != user.BusinessId || request.ConceptId == Guid.Empty || request.ExpenseAccountId == Guid.Empty)
            throw new ExpenseForbiddenException("El concepto está fuera de la empresa autenticada.");
        return store.SaveConceptAsync(user, request with
        {
            Name = Text(request.Name, 120, "Nombre"),
            WithholdingConceptCode = Optional(request.WithholdingConceptCode, 32)
        }, ct);
    }

    public Task<ExpensePage> ListAsync(ExpenseUserIdentity user, int page, int pageSize, string? search,
        Guid? conceptId, Guid? supplierId, DateOnly? from, DateOnly? to, string? status, string? payableStatus,
        CancellationToken ct = default)
    {
        Demand(user, ExpensePermissionCodes.Read);
        if (page < 1 || pageSize is < 1 or > 100 || to < from) throw new ExpenseValidationException("Los filtros del reporte no son válidos.");
        if (status is not null and not ("Accepted" or "Processed" or "CancellationPending" or "Cancelled" or "Returned"))
            throw new ExpenseValidationException("El estado del gasto no es válido.");
        if (payableStatus is not null and not ("Open" or "PartiallyPaid" or "Paid" or "Cancelled" or "None"))
            throw new ExpenseValidationException("El estado de la cuenta por pagar no es válido.");
        return store.ListAsync(user, page, pageSize, Optional(search, 120), conceptId, supplierId, from, to, status, payableStatus, ct);
    }

    public Task<ExpenseDetail?> GetAsync(ExpenseUserIdentity user, Guid expenseId, CancellationToken ct = default)
    {
        Demand(user, ExpensePermissionCodes.Read);
        if (expenseId == Guid.Empty) throw new ExpenseValidationException("El gasto no es válido.");
        return store.GetAsync(user, expenseId, ct);
    }

    public async Task<ExpenseCancellationAcceptance> CancelAsync(ExpenseUserIdentity user,
        Guid expenseId, CancelExpenseRequest request, CancellationToken ct = default)
    {
        Demand(user, ExpensePermissionCodes.Cancel);
        if (expenseId == Guid.Empty || request.CancellationId == Guid.Empty)
            throw new ExpenseValidationException("El gasto y la anulación son obligatorios.");
        var reason = Text(request.Reason, 300, "Motivo");
        var accepted = await store.CancelAsync(user, expenseId, request with { Reason = reason }, ct);
        if (!accepted.IdempotentReplay)
        {
            await signals.PublishAsync(new AccountingProcessingSignal(
                accepted.AccountingJobId, user.BusinessId, accepted.CancellationId,
                ExpenseDocumentTypes.Cancellation), ct);
            if (accepted.HasFiscalAdjustment)
                await fiscal.RequestGenerationAsync(user.BusinessId, accepted.CancellationId, ct);
        }
        return accepted;
    }

    public async Task<ExpenseAcceptance> ConfirmAsync(ExpenseUserIdentity user, string idempotencyKey,
        ConfirmExpenseRequest request, CancellationToken ct = default)
    {
        Demand(user, ExpensePermissionCodes.Create);
        if (request.BusinessId != user.BusinessId) throw new ExpenseForbiddenException("El gasto pertenece a otra empresa.");
        if (request.ExpenseId == Guid.Empty || request.SupplierId == Guid.Empty || request.ConceptId == Guid.Empty)
            throw new ExpenseValidationException("Gasto, proveedor y concepto son obligatorios.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new ExpenseValidationException("Idempotency-Key es obligatorio y admite máximo 160 caracteres.");
        if (request.IssuedAt == default || request.DueDate < request.IssuedAt)
            throw new ExpenseValidationException("Las fechas del documento no son válidas.");
        if (request.PurchaseEvidenceType is not (PurchaseEvidenceTypes.SupplierElectronicInvoice or
            PurchaseEvidenceTypes.BuyerElectronicSupportDocument or PurchaseEvidenceTypes.InternalReceiptVoucher))
            throw new ExpenseValidationException("El tipo de documento del gasto no es válido.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency != "COP") throw new ExpenseValidationException("Por ahora los gastos se registran en COP.");
        ExpenseAmounts amounts;
        try { amounts = ExpenseAmounts.Create(request.TaxExclusiveAmount, request.VatAmount); }
        catch (ExpenseRuleException error) { throw new ExpenseValidationException(error.Message); }
        var supplierDocumentNumber = Optional(request.SupplierDocumentNumber, 80);
        if (request.PurchaseEvidenceType == PurchaseEvidenceTypes.SupplierElectronicInvoice &&
            supplierDocumentNumber is null)
            throw new ExpenseValidationException("El número de la factura electrónica del proveedor es obligatorio.");
        var normalized = request with { CurrencyCode = currency,
            SupplierDocumentNumber = supplierDocumentNumber,
            Description = string.IsNullOrWhiteSpace(request.Description) ? "Gasto operativo" : Text(request.Description, 300, "Descripción"), EvidenceUrl = Optional(request.EvidenceUrl, 1000),
            WithholdingJurisdictionCode = Optional(request.WithholdingJurisdictionCode, 16) };
        var replay = await store.FindReplayAsync(user, idempotencyKey.Trim(), normalized, amounts, ct);
        if (replay is not null) return replay;
        var concept = await store.GetConceptAsync(user, request.ConceptId, ct);
        if (concept is null || !concept.IsActive)
            throw new ExpenseValidationException("El concepto de gasto no está activo.");
        var calculation = await withholding.CalculateAsync(user.TenantId, user.BusinessId,
            new WithholdingPreviewRequest(user.BusinessId, WithholdingDirections.Purchase,
                WithholdingRecognitionMoments.Accrual, request.SupplierId, concept.WithholdingConceptCode,
                normalized.WithholdingJurisdictionCode, amounts.TaxExclusiveAmount, amounts.VatAmount, request.IssuedAt), ct);
        var accepted = await store.AcceptAsync(user, idempotencyKey.Trim(), normalized, amounts, calculation, ct);
        if (!accepted.IdempotentReplay)
        {
            await signals.PublishAsync(new AccountingProcessingSignal(
                accepted.AccountingJobId ?? throw new InvalidOperationException("The expense has no accounting job."),
                user.BusinessId, accepted.ExpenseId, ExpenseDocumentTypes.Expense), ct);
            if (accepted.HasFiscalSupport)
                await fiscal.RequestGenerationAsync(user.BusinessId, accepted.ExpenseId, ct);
        }
        return accepted;
    }

    private static void Demand(ExpenseUserIdentity user, string permission)
    { if (!user.Permissions.Contains(permission)) throw new ExpenseForbiddenException($"Se requiere el permiso '{permission}'."); }
    private static string Text(string? value, int max, string label) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > max
            ? throw new ExpenseValidationException($"{label} es obligatorio y admite máximo {max} caracteres.") : value.Trim();
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null :
        value.Trim().Length > max ? throw new ExpenseValidationException($"El valor admite máximo {max} caracteres.") : value.Trim();
}

public sealed class ExpenseForbiddenException(string message) : Exception(message);
public sealed class ExpenseValidationException(string message) : Exception(message);
public sealed class ExpenseConflictException(string message) : Exception(message);
