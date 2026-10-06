using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Expenses;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Auraly.Application.Expenses;

public interface IExpenseStore
{
    Task<ExpenseResolution> ResolveAsync(ExpenseUserIdentity user, ConfirmExpenseRequest request, CancellationToken ct);
    Task<ExpenseAcceptance?> FindReplayAsync(ExpenseUserIdentity user, string idempotencyKey,
        ConfirmExpenseRequest request, ExpenseAmounts amounts, CancellationToken ct);
    Task<ExpenseConceptView?> GetConceptAsync(ExpenseUserIdentity user, Guid conceptId, CancellationToken ct);
    Task<ExpenseWorkspaceOptions> GetOptionsAsync(ExpenseUserIdentity user, CancellationToken ct, bool includeDirectories = true);
    Task<IReadOnlyList<ExpenseConceptView>> ListConceptsAsync(ExpenseUserIdentity user, bool includeInactive, CancellationToken ct);
    Task<ExpenseConceptView> SaveConceptAsync(ExpenseUserIdentity user, SaveExpenseConceptRequest request, CancellationToken ct);
    Task<ExpensePage> ListAsync(ExpenseUserIdentity user, int page, int pageSize, string? search, Guid? conceptId,
        Guid? supplierId, DateOnly? from, DateOnly? to, string? status, string? payableStatus, CancellationToken ct);
    Task<ExpenseDetail?> GetAsync(ExpenseUserIdentity user, Guid expenseId, CancellationToken ct);
    Task<ExpenseCancellationAcceptance> CancelAsync(ExpenseUserIdentity user, Guid expenseId,
        CancelExpenseRequest request, CancellationToken ct);
    Task<ExpenseAcceptance> AcceptAsync(ExpenseUserIdentity user, string idempotencyKey,
        ConfirmExpenseRequest request, ExpenseAmounts amounts, WithholdingCalculationSnapshot withholding, CancellationToken ct,
        ExpenseResolution? resolution = null);
}

public sealed class ExpenseService(IExpenseStore store, WithholdingService withholding,
    IAccountingProcessingSignalPublisher signals, Auraly.Application.Fiscal.FiscalProcessingCoordinator fiscal)
{
    public Task<ExpenseWorkspaceOptions> GetOptionsAsync(ExpenseUserIdentity user, CancellationToken ct = default, bool includeDirectories = true)
    { Demand(user, ExpensePermissionCodes.Read); return store.GetOptionsAsync(user, ct, includeDirectories); }

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
        if (request.ReasonOptionId == Guid.Empty ||
            (request.ReasonOptionId is not null && !string.IsNullOrWhiteSpace(request.Reason)))
            throw new ExpenseValidationException("Selecciona un único motivo de anulación válido.");
        var reason = request.ReasonOptionId is null ? Text(request.Reason, 300, "Motivo") : null;
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
        if (request.ExpenseId == Guid.Empty || request.SupplierId == Guid.Empty ||
            (request.Lines is null && (request.ConceptId is null || request.ConceptId == Guid.Empty)))
            throw new ExpenseValidationException("Gasto, proveedor y concepto son obligatorios.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new ExpenseValidationException("Idempotency-Key es obligatorio y admite máximo 160 caracteres.");
        if (request.IssuedAt == default || request.DueDate < request.IssuedAt)
            throw new ExpenseValidationException("Las fechas del documento no son válidas.");
        if (request.PurchaseEvidenceType is not (PurchaseEvidenceTypes.SupplierElectronicInvoice or
            PurchaseEvidenceTypes.BuyerElectronicSupportDocument or PurchaseEvidenceTypes.InternalReceiptVoucher))
            throw new ExpenseValidationException("El tipo de documento del gasto no es válido.");
        if (request.Lines is not null) ValidateLines(request);
        var currency = Text(request.CurrencyCode, 3, "Moneda").ToUpperInvariant();
        if (currency != "COP") throw new ExpenseValidationException("Por ahora los gastos se registran en COP.");
        ExpenseAmounts amounts;
        try { amounts = ExpenseAmounts.Create(request.Lines?.Sum(line => line.TaxExclusiveAmount) ?? request.TaxExclusiveAmount,
            request.Lines is null ? request.VatAmount : 0); }
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
        RequireWithholdingAdjustmentPermission(user, normalized.WithholdingAdjustments);
        if (normalized.Lines is null && normalized.WithholdingAdjustments is { Count: > 0 })
            throw new ExpenseValidationException("Registra el gasto por líneas para ajustar retenciones manualmente.");
        if (normalized.Lines is null && normalized.PurchaseEvidenceType == PurchaseEvidenceTypes.InternalReceiptVoucher && amounts.VatAmount > 0)
            throw new ExpenseValidationException("Registra este comprobante interno por líneas y selecciona el IVA como mayor valor del gasto.");
        ExpenseResolution? resolution = null;
        WithholdingCalculationSnapshot calculation;
        if (normalized.Lines is not null)
        {
            resolution = await store.ResolveAsync(user, normalized, ct);
            var preview = await CalculatePreviewAsync(user, normalized, resolution, ct);
            if (!preview.CanConfirm) throw new ExpenseValidationException(string.Join(" ", preview.Diagnostics));
            if (!string.Equals(normalized.CalculationHash, preview.CalculationHash, StringComparison.Ordinal))
                throw new ExpenseConflictException("El cálculo cambió o no ha sido revisado. Recalcula las retenciones y revisa el total antes de confirmar.");
            calculation = preview.Withholding;
            amounts = ExpenseAmounts.Create(resolution.Lines.Sum(l => l.TaxExclusiveAmount), resolution.Lines.Sum(l => l.VatAmount));
        }
        else
        {
            var concept = await store.GetConceptAsync(user, request.ConceptId!.Value, ct);
            if (concept is null || !concept.IsActive)
                throw new ExpenseValidationException("El concepto de gasto no está activo.");
            calculation = await withholding.CalculateAsync(user.TenantId, user.BusinessId,
                new WithholdingPreviewRequest(user.BusinessId, WithholdingDirections.Purchase,
                    WithholdingRecognitionMoments.Accrual, request.SupplierId, concept.WithholdingConceptCode,
                    normalized.WithholdingJurisdictionCode, amounts.TaxExclusiveAmount, amounts.VatAmount, request.IssuedAt), ct);
        }
        var accepted = await store.AcceptAsync(user, idempotencyKey.Trim(), normalized, amounts, calculation, ct, resolution);
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

    public async Task<ExpensePreview> PreviewAsync(ExpenseUserIdentity user, ConfirmExpenseRequest request,
        CancellationToken ct = default)
    {
        Demand(user, ExpensePermissionCodes.Create);
        if (request.BusinessId != user.BusinessId) throw new ExpenseForbiddenException("El gasto pertenece a otra empresa.");
        RequireWithholdingAdjustmentPermission(user, request.WithholdingAdjustments);
        if (request.SupplierId == Guid.Empty || request.IssuedAt == default || request.DueDate < request.IssuedAt)
            throw new ExpenseValidationException("Selecciona un proveedor y fechas válidas.");
        ValidateLines(request);
        var resolved = await store.ResolveAsync(user, request, ct);
        return await CalculatePreviewAsync(user, request, resolved, ct);
    }

    private async Task<ExpensePreview> CalculatePreviewAsync(ExpenseUserIdentity user,
        ConfirmExpenseRequest request, ExpenseResolution resolved, CancellationToken ct)
    {
        var manualAccounts = request.WithholdingAdjustments?
            .Where(item => item.Action == "Manual" && item.AccountId.HasValue)
            .Select(item => item.AccountId!.Value).Distinct().ToArray();
        var plan = await withholding.PrepareCalculationPlanAsync(user.TenantId, user.BusinessId,
            [request.SupplierId], ct, manualAccounts);
        var hasProfile = plan.Profiles.ContainsKey(request.SupplierId);
        (WithholdingCalculationSnapshot Calculation, IReadOnlyList<string> Diagnostics) result;
        try
        {
            result = withholding.CalculateDocument(plan, resolved.Lines.Select(line =>
                new WithholdingPreviewRequest(user.BusinessId, WithholdingDirections.Purchase,
                    WithholdingRecognitionMoments.Accrual, request.SupplierId, line.WithholdingConceptCode,
                    null, line.TaxExclusiveAmount, line.VatAmount, request.IssuedAt)).ToArray());
        }
        catch (Auraly.Commerce.Taxation.Domain.WithholdingRuleException error)
        { throw new ExpenseValidationException($"Revisa la configuración de retenciones: {error.Message}"); }
        WithholdingCalculationSnapshot finalCalculation;
        try
        {
            finalCalculation = withholding.ApplyAdjustments(plan, result.Calculation,
                request.WithholdingAdjustments, user.UserId, request.IssuedAt,
                resolved.Lines.Sum(line => line.VatAmount));
        }
        catch (TaxationValidationException error) { throw new ExpenseValidationException(error.Message); }
        var hasManualWithholding = request.WithholdingAdjustments?.Any(item => item.Action == "Manual") == true;
        IReadOnlyList<string> diagnostics = hasProfile ? result.Diagnostics :
            [hasManualWithholding
                ? "El proveedor no tiene perfil tributario; se aplicará únicamente la retención manual indicada."
                : "Falta el perfil tributario del proveedor. En Terceros → Proveedores → Retenciones y perfil tributario, configura si aplica retención, sus responsabilidades y jurisdicción antes de confirmar."];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            request.BusinessId, request.SupplierId, request.IssuedAt, request.DueDate, request.PurchaseEvidenceType,
            resolved.Lines, finalCalculation, diagnostics, hasProfile }))));
        return new(resolved.Lines, finalCalculation, hash, diagnostics, hasProfile || hasManualWithholding);
    }

    private static void RequireWithholdingAdjustmentPermission(ExpenseUserIdentity user,
        IReadOnlyList<WithholdingAdjustmentRequest>? adjustments)
    {
        if (adjustments is { Count: > 0 } &&
            !user.Permissions.Contains(TaxationPermissionCodes.ManageWithholdingRules))
            throw new ExpenseForbiddenException("No tienes permiso para ajustar retenciones manualmente.");
    }

    private static void ValidateLines(ConfirmExpenseRequest request)
    {
        if (request.Lines is not { Count: >= 1 and <= 100 })
            throw new ExpenseValidationException("El gasto debe tener entre 1 y 100 líneas.");
        if (request.ConceptId is not null || request.CostCenterId is not null ||
            request.WithholdingJurisdictionCode is not null)
            throw new ExpenseValidationException("En un gasto por líneas, el concepto y centro se eligen en cada línea y la jurisdicción se toma del perfil tributario.");
        foreach (var line in request.Lines)
        {
            if (line is null || line.ExpenseAccountId == Guid.Empty || line.TaxExclusiveAmount <= 0 ||
                line.TaxExclusiveAmount > 999999999999m || line.TaxExclusiveAmount != decimal.Round(line.TaxExclusiveAmount, 4))
                throw new ExpenseValidationException("Cada línea requiere una cuenta y una base positiva de máximo cuatro decimales.");
            _ = Text(line.Description, 300, "Descripción de la línea");
            if (line.TaxTreatment is not (PurchasingTaxTreatments.DeductibleInputVat or PurchasingTaxTreatments.CapitalizedCost))
                throw new ExpenseValidationException("El tratamiento del IVA no es válido.");
            _ = Optional(line.WithholdingConceptCode, 32);
        }
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
