using Auraly.Application.DocumentProcessing;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Domain.Expenses;

namespace Auraly.Application.Expenses;

public interface IExpenseStore
{
    Task<ExpenseWorkspaceOptions> GetOptionsAsync(ExpenseUserIdentity user, CancellationToken ct);
    Task<IReadOnlyList<ExpenseConceptView>> ListConceptsAsync(ExpenseUserIdentity user, bool includeInactive, CancellationToken ct);
    Task<ExpenseConceptView> SaveConceptAsync(ExpenseUserIdentity user, SaveExpenseConceptRequest request, CancellationToken ct);
    Task<ExpensePage> ListAsync(ExpenseUserIdentity user, int page, int pageSize, string? search, Guid? conceptId,
        Guid? supplierId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<ExpenseAcceptance> AcceptAsync(ExpenseUserIdentity user, string idempotencyKey,
        ConfirmExpenseRequest request, ExpenseAmounts amounts, WithholdingCalculationSnapshot withholding, CancellationToken ct);
}

public sealed class ExpenseService(IExpenseStore store, WithholdingService withholding,
    IDocumentProcessingSignalPublisher signals)
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
            Code = Text(request.Code, 32, "Código"), Name = Text(request.Name, 120, "Nombre"),
            WithholdingConceptCode = Optional(request.WithholdingConceptCode, 32)
        }, ct);
    }

    public Task<ExpensePage> ListAsync(ExpenseUserIdentity user, int page, int pageSize, string? search,
        Guid? conceptId, Guid? supplierId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        Demand(user, ExpensePermissionCodes.Read);
        if (page < 1 || pageSize is < 1 or > 100 || to < from) throw new ExpenseValidationException("Los filtros del reporte no son válidos.");
        return store.ListAsync(user, page, pageSize, Optional(search, 120), conceptId, supplierId, from, to, ct);
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
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency != "COP") throw new ExpenseValidationException("Por ahora los gastos se registran en COP.");
        ExpenseAmounts amounts;
        try { amounts = ExpenseAmounts.Create(request.TaxExclusiveAmount, request.VatAmount); }
        catch (ExpenseRuleException error) { throw new ExpenseValidationException(error.Message); }
        var normalized = request with { CurrencyCode = currency,
            SupplierDocumentNumber = Text(request.SupplierDocumentNumber, 80, "Número del proveedor"),
            Description = Text(request.Description, 300, "Descripción"), EvidenceUrl = Optional(request.EvidenceUrl, 1000),
            WithholdingJurisdictionCode = Optional(request.WithholdingJurisdictionCode, 16) };
        var options = await store.GetOptionsAsync(user, ct);
        var concept = options.Concepts.SingleOrDefault(x => x.ConceptId == request.ConceptId && x.IsActive)
            ?? throw new ExpenseValidationException("El concepto de gasto no está activo.");
        var calculation = await withholding.CalculateAsync(user.TenantId, user.BusinessId,
            new WithholdingPreviewRequest(user.BusinessId, WithholdingDirections.Purchase,
                WithholdingRecognitionMoments.Accrual, request.SupplierId, concept.WithholdingConceptCode,
                normalized.WithholdingJurisdictionCode, amounts.TaxExclusiveAmount, amounts.VatAmount, request.IssuedAt), ct);
        var accepted = await store.AcceptAsync(user, idempotencyKey.Trim(), normalized, amounts, calculation, ct);
        await signals.PublishAsync(new DocumentProcessingSignal(accepted.MovementId, user.BusinessId,
            accepted.ExpenseId, ExpenseDocumentTypes.Expense), ct);
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
