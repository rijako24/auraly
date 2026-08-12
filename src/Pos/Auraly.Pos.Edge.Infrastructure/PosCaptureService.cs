using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;

namespace Auraly.Pos.Edge.Infrastructure;

public interface IPosInventoryAvailabilityClient
{
    Task<InventoryAvailabilityResponse> CheckAvailabilityAsync(
        InventoryAvailabilityRequest request,
        CancellationToken cancellationToken = default);
}

public static class PosCaptureStatus
{
    public const string Added = "Added";
    public const string NotFound = "NotFound";
    public const string InsufficientInventory = "InsufficientInventory";
    public const string OfflineValidationRequired = "OfflineValidationRequired";
}

public sealed record PosCaptureResult(
    string Status,
    PosDraft? Draft,
    CapturedCatalogProduct? CapturedProduct,
    InventoryAvailabilityResponse? Availability)
{
    public bool Added => Status == PosCaptureStatus.Added;
}

public sealed class PosCaptureService(
    PosCatalogStore catalog,
    PosDraftStore drafts,
    IPosInventoryAvailabilityClient availability)
{
    public async Task<PosCaptureResult> CaptureAsync(
        string scannedValue,
        PosDraftScope scope,
        Guid? customerId,
        bool warehouseAllowsNegativeStock,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var captured = await catalog.CaptureAsync(scannedValue, cancellationToken);
        if (captured is null)
            return new PosCaptureResult(PosCaptureStatus.NotFound, null, null, null);

        var active = await drafts.GetOrCreateActiveAsync(scope, cancellationToken);
        var totalQuantity = active.Lines
            .Where(line => line.ProductId.Value == captured.Product.ProductId)
            .Sum(line => line.Quantity) + captured.Quantity;
        var inventory = await ValidateAsync(
            captured.Product.ProductId,
            scope.WarehouseId.Value,
            totalQuantity,
            warehouseAllowsNegativeStock,
            operationId,
            cancellationToken);
        if (inventory.Status != PosCaptureStatus.Added)
            return new PosCaptureResult(inventory.Status, active, captured, inventory.Response);

        var price = await catalog.ResolvePriceAsync(
            captured.Product.ProductId,
            customerId,
            totalQuantity,
            cancellationToken);
        var updated = await drafts.AddOrIncrementLineAsync(
            scope,
            new PosDraftLineInput(
                new ProductId(captured.Product.ProductId),
                captured.Product.ProductCode,
                captured.Product.Name,
                captured.Product.BaseUnitCode,
                captured.Product.TaxCode,
                captured.Product.TaxRate,
                captured.Quantity,
                price.BaseAmount,
                price.Amount,
                price.CurrencyCode,
                price.Source,
                price.PriceListId,
                price.PriceChannelId),
            cancellationToken);
        return new PosCaptureResult(PosCaptureStatus.Added, updated, captured, inventory.Response);
    }

    public async Task<PosCaptureResult> ChangeQuantityAsync(
        DraftId draftId,
        Guid lineId,
        decimal quantity,
        bool warehouseAllowsNegativeStock,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var current = await drafts.GetAsync(draftId, cancellationToken)
            ?? throw new KeyNotFoundException("The draft does not exist.");
        var line = current.Lines.SingleOrDefault(value => value.LineId == lineId)
            ?? throw new KeyNotFoundException("The draft line does not exist.");
        var totalQuantity = current.Lines
            .Where(value => value.ProductId == line.ProductId && value.LineId != lineId)
            .Sum(value => value.Quantity) + quantity;
        var inventory = await ValidateAsync(
            line.ProductId.Value,
            current.Scope.WarehouseId.Value,
            totalQuantity,
            warehouseAllowsNegativeStock,
            operationId,
            cancellationToken);
        if (inventory.Status != PosCaptureStatus.Added)
            return new PosCaptureResult(inventory.Status, current, null, inventory.Response);
        var updated = await drafts.SetQuantityAsync(
            draftId,
            lineId,
            quantity,
            cancellationToken);
        return new PosCaptureResult(PosCaptureStatus.Added, updated, null, inventory.Response);
    }

    private async Task<(string Status, InventoryAvailabilityResponse? Response)> ValidateAsync(
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        bool warehouseAllowsNegativeStock,
        Guid operationId,
        CancellationToken ct)
    {
        if (warehouseAllowsNegativeStock)
            return (PosCaptureStatus.Added, null);
        try
        {
            var response = await availability.CheckAvailabilityAsync(
                new InventoryAvailabilityRequest(productId, warehouseId, quantity, operationId),
                ct);
            return response.IsAvailable
                ? (PosCaptureStatus.Added, response)
                : (PosCaptureStatus.InsufficientInventory, response);
        }
        catch (HttpRequestException)
        {
            return (PosCaptureStatus.OfflineValidationRequired, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (PosCaptureStatus.OfflineValidationRequired, null);
        }
    }
}
