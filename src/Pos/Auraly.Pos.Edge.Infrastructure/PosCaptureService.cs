using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Sales;

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
    public async Task<OnlineSalesInventoryValidation> ValidateDraftInventoryAsync(
        DraftId draftId,
        bool warehouseAllowsNegativeStock,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetAsync(draftId, cancellationToken)
            ?? throw new KeyNotFoundException("The draft does not exist.");
        if (warehouseAllowsNegativeStock)
            return new OnlineSalesInventoryValidation(true, true, []);

        var issues = new List<OnlineSalesInventoryIssue>();
        foreach (var group in draft.Lines.GroupBy(line => line.ProductId))
        {
            InventoryAvailabilityResponse availabilityResult;
            try
            {
                availabilityResult = await availability.CheckAvailabilityAsync(
                    new InventoryAvailabilityRequest(
                        group.Key.Value, draft.Scope.WarehouseId.Value,
                        group.Sum(line => line.Quantity), operationId),
                    cancellationToken);
            }
            catch (HttpRequestException)
            {
                return new OnlineSalesInventoryValidation(false, false, []);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new OnlineSalesInventoryValidation(false, false, []);
            }

            var remaining = availabilityResult.AvailableQuantity;
            foreach (var line in group.OrderBy(line => line.Position))
            {
                if (line.Quantity > remaining)
                    issues.Add(new OnlineSalesInventoryIssue(
                        line.LineId, line.ProductId.Value, line.ProductCode,
                        line.Description, line.Quantity, Math.Max(0, remaining)));
                remaining = Math.Max(0, remaining - line.Quantity);
            }
        }
        return new OnlineSalesInventoryValidation(issues.Count == 0, true, issues);
    }

    public async Task<PosCaptureResult> CaptureAsync(
        string scannedValue,
        PosDraftScope scope,
        Guid? requestedCustomerId,
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
            active.CustomerId,
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
                price.PriceChannelId,
                AllowsFractionalSale: captured.Product.AllowsFractionalSale,
                DocumentUnitCost: captured.Product.UnitCost,
                AllowsDocumentCostOverride: !captured.Product.ManagesStock),
            cancellationToken);
        updated = await RepriceAsync(
            updated,
            catalog,
            drafts,
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
        var product = await catalog.GetByProductIdAsync(line.ProductId.Value, cancellationToken)
            ?? throw new KeyNotFoundException("The product does not exist in the local catalog.");
        if (!product.AllowsFractionalSale && quantity != decimal.Truncate(quantity))
            throw new InvalidOperationException("Este producto solo se vende en unidades completas.");
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
        updated = await RepriceAsync(
            updated,
            catalog,
            drafts,
            cancellationToken);
        return new PosCaptureResult(PosCaptureStatus.Added, updated, null, inventory.Response);
    }

    private static async Task<PosDraft> RepriceAsync(
        PosDraft draft,
        PosCatalogStore catalog,
        PosDraftStore drafts,
        CancellationToken cancellationToken)
    {
        var quantities = draft.Lines
            .GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        var resolved = new Dictionary<ProductId, PosResolvedPrice>();
        var updates = new List<PosDraftLinePriceUpdate>(draft.Lines.Count);
        foreach (var line in draft.Lines)
        {
            if (!resolved.TryGetValue(line.ProductId, out var price))
            {
                price = await catalog.ResolvePriceAsync(
                    line.ProductId.Value,
                    draft.CustomerId,
                    quantities[line.ProductId],
                    cancellationToken);
                resolved[line.ProductId] = price;
            }
            updates.Add(new PosDraftLinePriceUpdate(
                line.LineId,
                price.BaseAmount,
                price.Amount,
                price.CurrencyCode,
                price.Source,
                price.PriceChannelId));
        }
        return await drafts.AssignCustomerAndPricesAsync(
            draft.DraftId,
            draft.CustomerId,
            updates,
            cancellationToken);
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
