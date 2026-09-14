using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Sales;
using Auraly.Domain.Inventory;

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
}

public sealed record PosCaptureResult(
    string Status,
    PosDraft? Draft,
    CapturedCatalogProduct? CapturedProduct,
    InventoryAvailabilityResponse? Availability,
    decimal? MaximumQuantity = null)
{
    public bool Added => Status == PosCaptureStatus.Added;
}

public sealed class PosCaptureService(
    PosCatalogStore catalog,
    PosDraftStore drafts,
    PosDraftPricingService pricing,
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

        var descriptors = await catalog.InventoryDescriptorsAsync(
            draft.Lines.Select(line => line.ProductId.Value).Distinct().ToArray(),
            cancellationToken);
        if (descriptors.Count != draft.Lines.Select(line => line.ProductId.Value).Distinct().Count())
            throw new KeyNotFoundException("A product does not exist in the local catalog.");
        var facts = draft.Lines.Select(line =>
        {
            var descriptor = descriptors[line.ProductId.Value];
            return new InventoryDemandLine(
                line.LineId, line.ProductId.Value, descriptor.InventoryProductId,
                descriptor.InventoryFactor, line.Quantity,
                descriptor.ManagesStock || descriptor.InventoryProductId != line.ProductId.Value);
        }).ToArray();
        var lineById = draft.Lines.ToDictionary(line => line.LineId);
        var issues = new List<OnlineSalesInventoryIssue>();
        foreach (var demand in InventoryDemandResolver.Resolve(facts))
        {
            var representative = demand.Lines[0];
            InventoryAvailabilityResponse availabilityResult;
            try
            {
                availabilityResult = await availability.CheckAvailabilityAsync(
                    new InventoryAvailabilityRequest(
                        representative.ProductId, draft.Scope.WarehouseId.Value,
                        InventoryDemandResolver.InProductUnits(
                            demand.RequiredInventoryQuantity,
                            representative.InventoryFactor), operationId),
                    cancellationToken);
            }
            catch (HttpRequestException)
            {
                return new OnlineSalesInventoryValidation(true, false, []);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new OnlineSalesInventoryValidation(true, false, []);
            }

            var allocations = InventoryDemandResolver.AllocateWholeLines(
                demand.Lines,
                new Dictionary<Guid, decimal>
                {
                    [demand.InventoryProductId] =
                        availabilityResult.AvailableQuantity * representative.InventoryFactor
                });
            foreach (var allocation in allocations.Where(value => !value.CanReserve))
            {
                var line = lineById[allocation.Line.LineId];
                issues.Add(new OnlineSalesInventoryIssue(
                    line.LineId, line.ProductId.Value, line.ProductCode,
                    line.Description, line.Quantity,
                    Math.Max(0, InventoryDemandResolver.InProductUnits(
                        allocation.AvailableInventoryQuantity,
                        allocation.Line.InventoryFactor))));
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
        CancellationToken cancellationToken = default,
        decimal? requestedQuantity = null)
    {
        var captured = await catalog.CaptureAsync(scannedValue, cancellationToken);
        if (captured is null)
            return new PosCaptureResult(PosCaptureStatus.NotFound, null, null, null);
        if (requestedQuantity is <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        if (requestedQuantity is { } explicitQuantity)
            captured = captured with { Quantity = explicitQuantity };

        var active = await drafts.GetOrCreateActiveAsync(scope, cancellationToken);
        var inventoryDemand = await InventoryDemandAsync(
            active, captured.Product, captured.Quantity, null, cancellationToken);
        var inventory = await ValidateAsync(
            captured.Product.ProductId,
            scope.WarehouseId.Value,
            inventoryDemand.QuantityInSelectedUnits,
            inventoryDemand.ValidationRequired,
            warehouseAllowsNegativeStock,
            operationId,
            cancellationToken);
        if (inventory.Status != PosCaptureStatus.Added)
            return new PosCaptureResult(
                inventory.Status,
                active,
                captured,
                inventory.Response,
                MaximumSelectableQuantity(
                    inventory.Response,
                    inventoryDemand.ExistingQuantityInSelectedUnits,
                    captured.Product.AllowsFractionalSale));

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
                captured.Product.UnitPrice,
                captured.Product.UnitPrice,
                captured.Product.CurrencyCode,
                "Base",
                null,
                AllowsFractionalSale: captured.Product.AllowsFractionalSale,
                DocumentUnitCost: captured.Product.UnitCost,
                AllowsDocumentCostOverride: !captured.Product.ManagesStock),
            cancellationToken);
        updated = await pricing.RepriceAsync(updated.DraftId, updated.CustomerId, cancellationToken);
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
        var inventoryDemand = await InventoryDemandAsync(
            current, product, quantity, lineId, cancellationToken);
        var inventory = await ValidateAsync(
            line.ProductId.Value,
            current.Scope.WarehouseId.Value,
            inventoryDemand.QuantityInSelectedUnits,
            inventoryDemand.ValidationRequired,
            warehouseAllowsNegativeStock,
            operationId,
            cancellationToken);
        if (inventory.Status != PosCaptureStatus.Added)
            return new PosCaptureResult(
                inventory.Status,
                current,
                null,
                inventory.Response,
                MaximumSelectableQuantity(
                    inventory.Response,
                    inventoryDemand.ExistingQuantityInSelectedUnits,
                    product.AllowsFractionalSale));
        var updated = await drafts.SetQuantityAsync(
            draftId,
            lineId,
            quantity,
            cancellationToken);
        updated = await pricing.RepriceAsync(updated.DraftId, updated.CustomerId, cancellationToken);
        return new PosCaptureResult(PosCaptureStatus.Added, updated, null, inventory.Response);
    }

    private async Task<InventoryDemand> InventoryDemandAsync(
        PosDraft draft,
        PosCatalogItem selectedProduct,
        decimal selectedQuantity,
        Guid? excludedLineId,
        CancellationToken cancellationToken)
    {
        var inventoryProductId = selectedProduct.InventoryProductId ?? selectedProduct.ProductId;
        var selectedFactor = selectedProduct.InventoryFactor;
        if (selectedFactor <= 0)
            throw new InvalidDataException("The selected product inventory factor must be positive.");
        var family = await catalog.InventoryFamilyAsync(inventoryProductId, cancellationToken);
        var validationRequired = selectedProduct.ManagesStock ||
            inventoryProductId != selectedProduct.ProductId || family.Count > 1;
        var existingDemandLines = draft.Lines
            .Where(line => line.LineId != excludedLineId && family.ContainsKey(line.ProductId.Value))
            .Select(line => new InventoryDemandLine(
                line.LineId, line.ProductId.Value, inventoryProductId,
                family[line.ProductId.Value], line.Quantity, validationRequired))
            .ToArray();
        var demandLines = existingDemandLines
            .Append(new InventoryDemandLine(
                Guid.Empty, selectedProduct.ProductId, inventoryProductId,
                selectedFactor, selectedQuantity, validationRequired));
        var demand = InventoryDemandResolver.Resolve(demandLines).SingleOrDefault();
        var existingDemand = InventoryDemandResolver.Resolve(existingDemandLines).SingleOrDefault();
        return new InventoryDemand(
            demand is null ? selectedQuantity : InventoryDemandResolver.InProductUnits(
                demand.RequiredInventoryQuantity, selectedFactor),
            existingDemand is null ? 0m : InventoryDemandResolver.InProductUnits(
                existingDemand.RequiredInventoryQuantity, selectedFactor),
            validationRequired);
    }

    private static decimal? MaximumSelectableQuantity(
        InventoryAvailabilityResponse? response,
        decimal existingQuantityInSelectedUnits,
        bool allowsFractionalSale)
    {
        if (response is null) return null;
        var maximum = Math.Max(0m, response.AvailableQuantity - existingQuantityInSelectedUnits);
        return allowsFractionalSale ? maximum : decimal.Truncate(maximum);
    }

    private sealed record InventoryDemand(
        decimal QuantityInSelectedUnits,
        decimal ExistingQuantityInSelectedUnits,
        bool ValidationRequired);

    private async Task<(string Status, InventoryAvailabilityResponse? Response)> ValidateAsync(
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        bool managesStock,
        bool warehouseAllowsNegativeStock,
        Guid operationId,
        CancellationToken ct)
    {
        if (!managesStock || warehouseAllowsNegativeStock)
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
            return (PosCaptureStatus.Added, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (PosCaptureStatus.Added, null);
        }
    }
}
