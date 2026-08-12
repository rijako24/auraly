using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;

namespace Auraly.Application.Purchasing;

public interface IGoodsReceiptWorkspaceStore
{
    Task<GoodsReceiptWorkspaceOptions> GetOptionsAsync(PurchasingUserIdentity user, CancellationToken cancellationToken);
    Task<GoodsReceiptProductPage> FindProductsAsync(PurchasingUserIdentity user, Guid supplierId, string? search, bool includeUnassociated, int page, int pageSize, CancellationToken cancellationToken);
    Task<GoodsReceiptProductOption> AssociateProductAsync(PurchasingUserIdentity user, AssociateGoodsReceiptProductRequest request, CancellationToken cancellationToken);
    Task<GoodsReceiptPage> ListAsync(PurchasingUserIdentity user, string? search, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<GoodsReceiptDraft?> GetDraftAsync(PurchasingUserIdentity user, Guid draftId, CancellationToken cancellationToken);
    Task<GoodsReceiptDraft> SaveDraftAsync(PurchasingUserIdentity user, SaveGoodsReceiptDraftRequest request, GoodsReceiptCalculation? calculation, CancellationToken cancellationToken);
    Task DeleteDraftAsync(PurchasingUserIdentity user, Guid draftId, string concurrencyToken, CancellationToken cancellationToken);
}

public sealed class GoodsReceiptWorkspaceService(IGoodsReceiptWorkspaceStore store)
{
    public Task<GoodsReceiptWorkspaceOptions> GetOptionsAsync(
        PurchasingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        return store.GetOptionsAsync(user, cancellationToken);
    }

    public Task<GoodsReceiptProductPage> FindProductsAsync(
        PurchasingUserIdentity user, Guid supplierId, string? search,
        bool includeUnassociated, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        if (supplierId == Guid.Empty) throw new PurchasingValidationException("SupplierId is required.");
        ValidatePage(page, pageSize);
        return store.FindProductsAsync(user, supplierId, Normalize(search, 160), includeUnassociated, page, pageSize, cancellationToken);
    }

    public Task<GoodsReceiptProductOption> AssociateProductAsync(
        PurchasingUserIdentity user, AssociateGoodsReceiptProductRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(user, "catalog.costs.manage");
        if (request.SupplierId == Guid.Empty) throw new PurchasingValidationException("SupplierId is required.");
        if (request.ProductId == Guid.Empty) throw new PurchasingValidationException("ProductId is required.");
        var presentation = Normalize(request.PurchasePresentationName, 80)
            ?? throw new PurchasingValidationException("PurchasePresentationName is required.");
        if (request.UnitsPerPresentation <= 0)
            throw new PurchasingValidationException("UnitsPerPresentation must be greater than zero.");
        return store.AssociateProductAsync(user, request with
        {
            SupplierProductCode = Normalize(request.SupplierProductCode, 80),
            PurchasePresentationName = presentation
        }, cancellationToken);
    }

    public Task<GoodsReceiptPage> ListAsync(
        PurchasingUserIdentity user, string? search, string? status,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        ValidatePage(page, pageSize);
        var normalizedStatus = Normalize(status, 24);
        if (normalizedStatus is not null &&
            normalizedStatus is not ("Draft" or "Accepted" or "Processed"))
            throw new PurchasingValidationException("Status is not valid.");
        return store.ListAsync(user, Normalize(search, 160), normalizedStatus, page, pageSize, cancellationToken);
    }

    public Task<GoodsReceiptDraft?> GetDraftAsync(
        PurchasingUserIdentity user, Guid draftId, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        if (draftId == Guid.Empty) throw new PurchasingValidationException("DraftId is required.");
        return store.GetDraftAsync(user, draftId, cancellationToken);
    }

    public Task<GoodsReceiptDraft> SaveDraftAsync(
        PurchasingUserIdentity user, SaveGoodsReceiptDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        if (user.BusinessId != request.BusinessId)
            throw new PurchasingForbiddenException("The draft belongs to another business.");
        if (request.DraftId == Guid.Empty) throw new PurchasingValidationException("DraftId is required.");
        if (request.ReceivedAt == default) throw new PurchasingValidationException("ReceivedAt is required.");
        if (request.CreatesPayable && request.DueDate is null)
            throw new PurchasingValidationException("DueDate is required for a credit purchase.");
        if (request.DueDate < request.ReceivedAt)
            throw new PurchasingValidationException("DueDate cannot be earlier than ReceivedAt.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3) throw new PurchasingValidationException("CurrencyCode must contain three characters.");

        GoodsReceiptCalculation? calculation = null;
        var normalizedLines = GoodsReceiptLineNormalizer.Normalize(request.Lines);
        if (normalizedLines.Length > 0)
        {
            if (request.SupplierId is null)
                throw new PurchasingValidationException("A supplier is required before adding products.");
            try
            {
                calculation = GoodsReceiptCalculator.Calculate(normalizedLines.Select(line => (
                    line.LineNumber, line.ProductId, line.Description, line.Quantity,
                    line.UnitCost, line.DiscountAmount, line.TaxCode, line.TaxRate,
                    ParseTaxTreatment(line.TaxTreatment))));
            }
            catch (ArgumentException exception)
            {
                throw new PurchasingValidationException(exception.Message, exception);
            }
        }

        return store.SaveDraftAsync(user, request with
        {
            CurrencyCode = currency,
            SupplierInvoiceNumber = Normalize(request.SupplierInvoiceNumber, 80),
            Notes = Normalize(request.Notes, 1000),
            Lines = normalizedLines
        }, calculation, cancellationToken);
    }

    public Task DeleteDraftAsync(
        PurchasingUserIdentity user, Guid draftId, string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.CreateGoodsReceipts);
        if (draftId == Guid.Empty) throw new PurchasingValidationException("DraftId is required.");
        if (string.IsNullOrWhiteSpace(concurrencyToken))
            throw new PurchasingValidationException("ConcurrencyToken is required.");
        return store.DeleteDraftAsync(user, draftId, concurrencyToken, cancellationToken);
    }

    private static PurchaseTaxTreatment ParseTaxTreatment(string value) =>
        Enum.TryParse<PurchaseTaxTreatment>(value, false, out var treatment) && Enum.IsDefined(treatment)
            ? treatment
            : throw new PurchasingValidationException("TaxTreatment is not valid.");

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new PurchasingValidationException($"The value exceeds {maximumLength} characters.");
        return normalized;
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1) throw new PurchasingValidationException("Page must be at least one.");
        if (pageSize is < 1 or > 100)
            throw new PurchasingValidationException("PageSize must be between one and one hundred.");
    }

    private static void Require(PurchasingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PurchasingForbiddenException($"Permission '{permission}' is required.");
    }
}
