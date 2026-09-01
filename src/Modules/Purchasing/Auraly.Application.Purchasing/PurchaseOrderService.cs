using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;

namespace Auraly.Application.Purchasing;

public interface IPurchaseOrderStore
{
    Task<PurchaseOrderPage> ListAsync(PurchasingUserIdentity user, string? search, string? status,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<PurchaseOrderDetail?> GetAsync(PurchasingUserIdentity user, Guid id, CancellationToken cancellationToken);
    Task<PurchaseOrderReceiptSource?> GetReceiptSourceAsync(PurchasingUserIdentity user, Guid id, CancellationToken cancellationToken);
    Task<PurchaseOrderDetail> SaveDraftAsync(PurchasingUserIdentity user, SavePurchaseOrderDraftRequest request,
        PurchaseOrderCalculation? calculation, CancellationToken cancellationToken);
    Task<PurchaseOrderConfirmation> ConfirmAsync(PurchasingUserIdentity user, string idempotencyKey,
        ConfirmPurchaseOrderRequest request, PurchaseOrderCalculation calculation, CancellationToken cancellationToken);
    Task CloseAsync(PurchasingUserIdentity user, Guid id, ClosePurchaseOrderRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseOrderSuggestionInput>> SuggestionInputsAsync(
        PurchasingUserIdentity user, Guid warehouseId, Guid supplierId,
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}

public sealed record PurchaseOrderSuggestionInput(
    Guid ProductId, decimal Rotation30Days, decimal Rotation90Days,
    decimal DailyDemand90Days, decimal CurrentStock, decimal IncomingQuantity,
    string PresentationName, decimal UnitsPerPresentation, DateTimeOffset? RotationCalculatedAt);

public sealed class PurchaseOrderService(IPurchaseOrderStore store)
{
    public Task<PurchaseOrderPage> ListAsync(PurchasingUserIdentity user, string? search,
        string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadPurchaseOrders);
        ValidatePage(page, pageSize);
        if (status is not null && status is not (PurchaseOrderStatuses.Draft or PurchaseOrderStatuses.Open or
            PurchaseOrderStatuses.PartiallyReceived or PurchaseOrderStatuses.Received or
            PurchaseOrderStatuses.Closed or PurchaseOrderStatuses.Cancelled))
            throw new PurchasingValidationException("Purchase-order status is invalid.");
        return store.ListAsync(user, Normalize(search, 160), status, page, pageSize, cancellationToken);
    }

    public Task<PurchaseOrderDetail?> GetAsync(PurchasingUserIdentity user, Guid id,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadPurchaseOrders);
        if (id == Guid.Empty) throw new PurchasingValidationException("PurchaseOrderId is required.");
        return store.GetAsync(user, id, cancellationToken);
    }

    public Task<PurchaseOrderReceiptSource?> GetReceiptSourceAsync(PurchasingUserIdentity user, Guid id,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadGoodsReceipts);
        Require(user, PurchasingPermissionCodes.ReadPurchaseOrders);
        if (id == Guid.Empty) throw new PurchasingValidationException("PurchaseOrderId is required.");
        return store.GetReceiptSourceAsync(user, id, cancellationToken);
    }

    public Task<PurchaseOrderDetail> SaveDraftAsync(PurchasingUserIdentity user,
        SavePurchaseOrderDraftRequest request, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.CreatePurchaseOrders);
        Scope(user, request.BusinessId);
        ValidateHeader(request.PurchaseOrderId, request.OrderedAt, request.ExpectedAt, request.CurrencyCode);
        var lines = Normalize(request.Lines);
        var calculation = lines.Length == 0 ? null : Calculate(lines);
        return store.SaveDraftAsync(user, request with
        {
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            Notes = Normalize(request.Notes, 1000), Lines = lines
        }, calculation, cancellationToken);
    }

    public Task<PurchaseOrderConfirmation> ConfirmAsync(PurchasingUserIdentity user, string idempotencyKey,
        ConfirmPurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.CreatePurchaseOrders);
        Require(user, PurchasingPermissionCodes.ConfirmPurchaseOrders);
        Scope(user, request.BusinessId);
        ValidateHeader(request.PurchaseOrderId, request.OrderedAt, request.ExpectedAt, request.CurrencyCode);
        if (request.WarehouseId == Guid.Empty || request.SupplierId == Guid.Empty)
            throw new PurchasingValidationException("WarehouseId and SupplierId are required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new PurchasingValidationException("A valid Idempotency-Key is required.");
        var lines = Normalize(request.Lines);
        var calculation = Calculate(lines);
        return store.ConfirmAsync(user, idempotencyKey.Trim(), request with
        {
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            Notes = Normalize(request.Notes, 1000), Lines = lines
        }, calculation, cancellationToken);
    }

    public Task CloseAsync(PurchasingUserIdentity user, Guid id, ClosePurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ClosePurchaseOrders);
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) ||
            string.IsNullOrWhiteSpace(request.ConcurrencyToken))
            throw new PurchasingValidationException("A purchase order, reason and concurrency token are required.");
        return store.CloseAsync(user, id, request with { Reason = Normalize(request.Reason, 500)! }, cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseOrderSuggestion>> SuggestionsAsync(
        PurchasingUserIdentity user, PurchaseOrderSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.CreatePurchaseOrders);
        Scope(user, request.BusinessId);
        if (request.WarehouseId == Guid.Empty || request.SupplierId == Guid.Empty ||
            request.ProductIds.Count is < 1 or > 100 || request.ProductIds.Any(x => x == Guid.Empty) ||
            request.ProductIds.Distinct().Count() != request.ProductIds.Count ||
            request.TargetCoverageDays is < 1 or > 90)
            throw new PurchasingValidationException("The purchase-order suggestion request is invalid.");
        var inputs = await store.SuggestionInputsAsync(user, request.WarehouseId,
            request.SupplierId, request.ProductIds, cancellationToken);
        return inputs.Select(input =>
        {
            var forecast = PurchaseOrderCalculator.ForecastDailyDemand(
                input.Rotation30Days, input.Rotation90Days);
            var suggested = PurchaseOrderCalculator.Suggest(forecast,
                input.CurrentStock, input.IncomingQuantity, input.UnitsPerPresentation,
                request.TargetCoverageDays);
            return new PurchaseOrderSuggestion(input.ProductId, request.TargetCoverageDays,
                input.Rotation30Days, input.Rotation90Days, input.DailyDemand90Days,
                forecast, input.CurrentStock, input.IncomingQuantity, input.PresentationName,
                input.UnitsPerPresentation, suggested.Quantity,
                suggested.PresentationQuantity, input.RotationCalculatedAt);
        }).ToArray();
    }

    private static PurchaseOrderLineRequest[] Normalize(IReadOnlyCollection<PurchaseOrderLineRequest> lines) =>
        lines.OrderBy(x => x.LineNumber).Select((line, index) => line with
        {
            LineNumber = index + 1, Description = line.Description.Trim(),
            TaxCode = line.TaxCode.Trim(), TaxTreatment = line.TaxTreatment.Trim(),
            PresentationName = string.IsNullOrWhiteSpace(line.PresentationName) ? "Unidad" : line.PresentationName.Trim()
        }).ToArray();

    private static PurchaseOrderCalculation Calculate(PurchaseOrderLineRequest[] lines)
    {
        try
        {
            if (lines.Any(x => x.PresentationQuantity <= 0 || x.UnitsPerPresentation <= 0 ||
                x.OrderedQuantity != x.PresentationQuantity * x.UnitsPerPresentation))
                throw new ArgumentException("Presentation quantity must reconcile with ordered quantity.");
            return PurchaseOrderCalculator.Calculate(lines.Select(x => (x.LineId, x.LineNumber,
                x.ProductId, x.Description, x.OrderedQuantity, x.UnitCost, x.DiscountAmount,
                x.TaxCode, x.TaxRate, x.TaxTreatment)));
        }
        catch (ArgumentException exception) { throw new PurchasingValidationException(exception.Message, exception); }
    }

    private static void ValidateHeader(Guid id, DateTimeOffset orderedAt, DateTimeOffset? expectedAt, string currency)
    {
        if (id == Guid.Empty || orderedAt == default) throw new PurchasingValidationException("PurchaseOrderId and OrderedAt are required.");
        if (expectedAt < orderedAt) throw new PurchasingValidationException("ExpectedAt cannot be earlier than OrderedAt.");
        if (currency.Trim().Length != 3) throw new PurchasingValidationException("CurrencyCode must contain three characters.");
    }
    private static void Scope(PurchasingUserIdentity user, Guid businessId)
    { if (user.BusinessId != businessId) throw new PurchasingForbiddenException("The purchase order belongs to another business."); }
    private static void Require(PurchasingUserIdentity user, string permission)
    { if (!user.Permissions.Contains(permission)) throw new PurchasingForbiddenException($"Permission '{permission}' is required."); }
    private static void ValidatePage(int page, int size)
    { if (page < 1 || size is < 1 or > 100) throw new PurchasingValidationException("Page and PageSize are invalid."); }
    private static string? Normalize(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.Length > max) throw new PurchasingValidationException($"The value exceeds {max} characters.");
        return result;
    }
}
