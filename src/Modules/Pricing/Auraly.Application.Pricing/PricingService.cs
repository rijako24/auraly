using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Pricing;
using Auraly.Domain.Pricing;

namespace Auraly.Application.Pricing;

public sealed record PriceProposalSource(
    Guid ProposalId, Guid ProductId, decimal? ObservedUnitCost,
    string? CostBasisType, decimal SalesTaxRate, string Status,
    byte[] RowVersion, bool IsManual);

public sealed record PreparedPricePublication(
    Guid ProposalId, Guid ProductId, decimal? CostBasisAmount,
    string? CostBasisType, string InputMode, decimal? TargetMarginPercent, decimal SalePrice,
    decimal? EffectiveMarginPercent, decimal RoundingIncrement,
    string RoundingMode, byte[] ExpectedRowVersion, bool IsManual, decimal SalesTaxRate);

public sealed record PreparedDirectProductPricePublication(
    Guid ProductId, decimal? CostBasisAmount, string? CostBasisType, string InputMode,
    decimal? TargetMarginPercent, decimal SalePrice, decimal? EffectiveMarginPercent,
    decimal RoundingIncrement, string RoundingMode, decimal SalesTaxRate);

public sealed record PendingPricePublicationSelection(
    IReadOnlyCollection<Guid>? ProductIds,
    string? Search,
    Guid? SupplierId,
    Guid? SourceDocumentId);

public sealed record PricePublicationStoreResult(
    PublishPricesResult Result,
    IReadOnlyList<Guid> TargetBusinessIds);

public sealed record PriceChannelReportProductSource(
    Guid ProductId, string ProductCode, string ProductName,
    decimal PublicAmount, string CurrencyCode,
    Guid? ProductCategoryId, Guid? ProductBrandId,
    IReadOnlyCollection<Guid> ProductCategoryAncestorIds,
    decimal AverageCost, decimal LatestCost, decimal? TargetMarginPercent);

public sealed record PriceChannelReportSource(
    Guid PriceChannelId, string Code, string Name, string Strategy,
    decimal? Value, bool IsActive,
    IReadOnlyCollection<PriceChannelReportProductSource> Products,
    IReadOnlyCollection<PriceChannelTierRule> Tiers,
    IReadOnlyCollection<PriceChannelExclusionRule> Exclusions);

public interface IPricingStore
{
    Task<PriceRevisionPage> ListAsync(PricingUserIdentity user, PriceRevisionQuery query, CancellationToken ct);
    Task<PriceProposalSource?> GetProposalAsync(PricingUserIdentity user, Guid proposalId, CancellationToken ct);
    Task<IReadOnlyList<PreparedPricePublication>> GetPendingPublicationsAsync(
        PricingUserIdentity user, PendingPricePublicationSelection selection, CancellationToken ct);
    Task<ReviewedPricePreparation> ReviewAsync(PricingUserIdentity user, Guid proposalId, PriceCalculationResult calculation, decimal expectedSalesTaxRate, byte[] expectedRowVersion, CancellationToken ct);
    Task RejectAsync(PricingUserIdentity user, Guid proposalId, byte[] expectedRowVersion, string? reason, CancellationToken ct);
    Task<PricePublicationStoreResult> PublishAsync(PricingUserIdentity user, IReadOnlyList<PreparedPricePublication> values, DateTimeOffset now, CancellationToken ct);
    Task<ProductPricingContext?> GetProductContextAsync(PricingUserIdentity user, Guid productId, DateTimeOffset asOf, CancellationToken ct);
    Task<PreparedProductPrice> SavePreparedProductAsync(PricingUserIdentity user, PreparedDirectProductPricePublication value, DateTimeOffset now, CancellationToken ct);
    Task<ProductPriceHistoryPage> HistoryAsync(PricingUserIdentity user, Guid productId, int page, int pageSize, string? activityType, CancellationToken ct);
    Task<PriceChannelReportSource?> GetChannelReportSourceAsync(
        PricingUserIdentity user, Guid priceChannelId, CancellationToken ct);
}

public sealed class PricingService(
    IPricingStore store,
    TimeProvider timeProvider,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<PriceRevisionPage> ListAsync(PricingUserIdentity user, PriceRevisionQuery query, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.Read);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new PricingValidationException("La página y la cantidad de registros no son válidas.");
        if (query.Status is not null && query.Status is not ("Pending" or "PendingReview" or "Approved" or "Published" or "Rejected" or "Superseded"))
            throw new PricingValidationException("El estado de la propuesta de precio no es válido.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120) }, ct);
    }

    public PriceCalculationResult Calculate(PricingUserIdentity user, PriceCalculationRequest request)
    {
        if (!user.Permissions.Contains(PricingPermissionCodes.Read) &&
            !user.Permissions.Contains(PricingPermissionCodes.ManageSegments))
            throw new PricingForbiddenException("No tienes permiso para calcular precios.");
        Require(user, PricingPermissionCodes.ReadCostBasis);
        return CalculateCore(request);
    }

    public async Task<ReviewedPricePreparation> ReviewAsync(PricingUserIdentity user, Guid proposalId, ReviewPriceProposalRequest request, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.ReviewProposals);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        var source = await RequiredProposalAsync(user, proposalId, ct);
        EnsureReviewable(source);
        var calculation = CalculateForSource(source, request.InputMode,
            request.TargetMarginPercent, request.SalePrice,
            request.RoundingIncrement, request.RoundingMode);
        return await store.ReviewAsync(
            user, proposalId, calculation, source.SalesTaxRate, DecodeToken(request.ConcurrencyToken), ct);
    }

    public async Task RejectAsync(PricingUserIdentity user, Guid proposalId, RejectPriceProposalRequest request, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.ReviewProposals);
        var source = await RequiredProposalAsync(user, proposalId, ct);
        EnsureReviewable(source);
        await store.RejectAsync(user, proposalId, DecodeToken(request.ConcurrencyToken), Normalize(request.Reason, 500), ct);
    }

    public async Task<PublishPricesResult> PublishAsync(PricingUserIdentity user, PublishPricesRequest request, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.PublishPrices);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        var productIds = request.ProductIds?.Distinct().ToArray();
        var publishesAllMatching = request.AllMatching is not null;
        if (publishesAllMatching == (productIds is { Length: > 0 }))
            throw new PricingValidationException(
                "Selecciona productos específicos o todos los precios preparados que coinciden con los filtros.");
        if (publishesAllMatching || productIds!.Length > 1)
            Require(user, PricingPermissionCodes.BulkPublish);
        var filter = request.AllMatching;
        var prepared = await store.GetPendingPublicationsAsync(
            user,
            new(
                productIds,
                Normalize(filter?.Search, 120),
                filter?.SupplierId,
                filter?.SourceDocumentId),
            ct);
        if (productIds is not null && prepared.Count != productIds.Length)
            throw new PricingConflictException(
                "Uno o más precios seleccionados cambiaron o ya fueron publicados.");
        if (prepared.Count == 0)
            throw new PricingValidationException(
                "La selección no contiene precios preparados pendientes.");
        return await PublishPreparedAsync(user, prepared, ct);
    }

    private async Task<PublishPricesResult> PublishPreparedAsync(
        PricingUserIdentity user,
        IReadOnlyList<PreparedPricePublication> prepared,
        CancellationToken ct)
    {
        // Validate the durable preparation before any publication effects. Reuse
        // the preparation calculator; never silently change an approved price.
        foreach (var value in prepared)
        {
            var calculation = CalculateForOptionalCost(value.CostBasisAmount,
                value.SalesTaxRate, value.InputMode, value.TargetMarginPercent,
                value.SalePrice, value.RoundingIncrement, value.RoundingMode);
            if (calculation.RoundedSalePrice != value.SalePrice ||
                calculation.EffectiveMarginPercent != value.EffectiveMarginPercent)
                throw new PricingConflictException(
                    "La publicación fue rechazada porque el costo, el IVA, el margen y el precio preparado no coinciden. Vuelve a preparar el precio e intenta publicarlo de nuevo.");
        }
        var outcome = await store.PublishAsync(user, prepared, timeProvider.GetUtcNow(), ct);
        foreach (var businessId in outcome.TargetBusinessIds)
            await synchronization.DispatchPendingAsync(user.TenantId, businessId, CancellationToken.None);
        return outcome.Result;
    }

    public async Task<ProductPricingContext> GetProductContextAsync(PricingUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.Read);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        return await store.GetProductContextAsync(user, productId, timeProvider.GetUtcNow(), ct)
            ?? throw new PricingNotFoundException("No se encontró el producto.");
    }

    public async Task<PreparedProductPrice> SavePreparedProductAsync(
        PricingUserIdentity user, Guid productId, PublishProductPriceRequest request, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.PreparePrices);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        var context = await store.GetProductContextAsync(user, productId, timeProvider.GetUtcNow(), ct)
            ?? throw new PricingNotFoundException("No se encontró el producto.");
        if (!context.IsCostLinked && request.CostBasisAmount is <= 0m)
            throw new PricingValidationException("El costo manual debe ser mayor que cero.");
        if (context.IsCostLinked && request.CostBasisAmount is { } requestedCost
            && requestedCost != context.CostBasisAmount)
            throw new PricingValidationException(
                "El costo del producto vinculado depende del producto principal y no se puede editar.");

        var costBasis = context.IsCostLinked
            ? context.CostBasisAmount
            : request.CostBasisAmount ?? context.CostBasisAmount;
        if (costBasis is null && request.InputMode == PriceInputModes.Margin)
            throw new PricingValidationException("Se requiere un costo para calcular el margen.");

        var costBasisType = context.IsCostLinked ||
            (context.CostBasisOrigin == "Composition" && costBasis == context.CostBasisAmount)
            ? context.CostBasisOrigin
            : request.CostBasisAmount.HasValue
            ? "Manual"
            : context.CostBasisOrigin;
        var calculation = CalculateForOptionalCost(
            costBasis, context.SalesTaxRate, request.InputMode,
            request.TargetMarginPercent, request.SalePrice,
            request.RoundingIncrement, request.RoundingMode);
        var prepared = await store.SavePreparedProductAsync(user, new(
            productId,
            costBasis,
            costBasisType,
            calculation.InputMode,
            costBasis is null ? null : calculation.TargetMarginPercent,
            calculation.RoundedSalePrice,
            costBasis is null ? null : calculation.EffectiveMarginPercent,
            calculation.RoundingIncrement,
            calculation.RoundingMode, context.SalesTaxRate), timeProvider.GetUtcNow(), ct);
        return prepared;
    }
    public Task<ProductPriceHistoryPage> HistoryAsync(PricingUserIdentity user, Guid productId, int page, int pageSize, string? activityType, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.ReadHistory);
        if (page < 1 || pageSize is < 1 or > 100)
            throw new PricingValidationException("La página o la cantidad de registros supera el rango permitido.");
        if (activityType is not null && activityType is not ("Preparation" or "Publication"))
            throw new PricingValidationException("Selecciona preparación o publicación para consultar la actividad.");
        return store.HistoryAsync(user, productId, page, pageSize, activityType, ct);
    }

    public async Task<PriceChannelProductReport> ChannelProductReportAsync(
        PricingUserIdentity user, Guid priceChannelId, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.ReadSegments);
        if (priceChannelId == Guid.Empty)
            throw new PricingValidationException("El canal de precios es obligatorio.");
        var source = await store.GetChannelReportSourceAsync(user, priceChannelId, ct)
            ?? throw new PricingNotFoundException("El canal de precios no existe en el negocio seleccionado.");
        var channel = new PriceChannelRule(source.PriceChannelId, source.Strategy, source.Value);
        var rows = new List<PriceChannelProductReportRow>();
        foreach (var product in source.Products.OrderBy(value => value.ProductName, StringComparer.CurrentCulture))
        {
            var context = new PriceChannelProductContext(
                product.ProductId, product.ProductCategoryId, product.ProductBrandId,
                product.ProductCategoryAncestorIds, product.CurrencyCode,
                product.AverageCost, product.LatestCost, product.TargetMarginPercent);
            if (PriceChannelResolver.IsExcluded(source.PriceChannelId, context, source.Exclusions))
                continue;

            IEnumerable<decimal> quantities = string.Equals(
                    source.Strategy, PriceChannelStrategies.TieredProductPrice,
                    StringComparison.Ordinal)
                ? source.Tiers.Where(value => value.ProductId == product.ProductId)
                    .Select(value => value.MinimumQuantity).Append(1m).Distinct().Order()
                : new[] { 1m };
            foreach (var quantity in quantities)
            {
                var resolution = PriceChannelResolver.Resolve(
                    source.PriceChannelId, product.PublicAmount, quantity, context,
                    [channel], source.Tiers, source.Exclusions);
                rows.Add(new(
                    product.ProductId, product.ProductCode, product.ProductName, quantity,
                    product.PublicAmount, resolution.Amount ?? product.PublicAmount,
                    product.CurrencyCode,
                    resolution.Applied ? "Canal" : "Precio público"));
            }
        }
        return new(source.PriceChannelId, source.Code, source.Name, source.Strategy,
            source.IsActive, rows);
    }

    private static PriceCalculationResult CalculateCore(PriceCalculationRequest request)
    {
        if (request.CostBasisAmount <= 0) throw new PricingValidationException("El costo base debe ser mayor que cero.");
        if (request.SalesTaxRate is < 0 or > 100) throw new PricingValidationException("La tarifa del impuesto de venta no es válida.");
        if (!PriceInputModes.IsSupported(request.InputMode)) throw new PricingValidationException("El modo de cálculo del precio no es válido.");
        if (!PricingRoundingModes.IsSupported(request.RoundingMode)) throw new PricingValidationException("El modo de redondeo no es válido.");
        if (request.RoundingIncrement <= 0) throw new PricingValidationException("El incremento de redondeo debe ser mayor que cero.");
        decimal raw;
        decimal? target;
        try
        {
            if (request.InputMode == PriceInputModes.Margin)
            {
                if (request.TargetMarginPercent is null or < 0 or >= 100)
                    throw new PricingValidationException("El margen objetivo debe estar entre 0 % y menos de 100 %.");
                target = request.TargetMarginPercent;
                raw = PriceMargin.CalculateGrossSalePrice(request.CostBasisAmount, target.Value, request.SalesTaxRate);
            }
            else
            {
                if (request.SalePrice is null or <= 0) throw new PricingValidationException("El precio de venta debe ser mayor que cero.");
                raw = request.SalePrice.Value;
                target = PriceMargin.CalculateMarginPercentFromGross(request.CostBasisAmount, raw, request.SalesTaxRate);
            }
            var rounded = PriceMargin.RoundPrice(raw, request.RoundingIncrement, request.RoundingMode);
            return new(request.CostBasisAmount, request.InputMode, target, raw, rounded,
                PriceMargin.CalculateMarginPercentFromGross(request.CostBasisAmount, rounded, request.SalesTaxRate),
                request.RoundingIncrement, request.RoundingMode);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new PricingValidationException(exception.Message, exception);
        }
    }

    private static PriceCalculationResult CalculateForSource(
        PriceProposalSource source, string inputMode, decimal? targetMarginPercent,
        decimal? salePrice, decimal roundingIncrement, string roundingMode)
    {
        return CalculateForOptionalCost(
            source.ObservedUnitCost, source.SalesTaxRate, inputMode,
            targetMarginPercent, salePrice, roundingIncrement, roundingMode);
    }

    private static PriceCalculationResult CalculateForOptionalCost(
        decimal? costBasisAmount, decimal salesTaxRate, string inputMode,
        decimal? targetMarginPercent, decimal? salePrice,
        decimal roundingIncrement, string roundingMode)
    {
        if (costBasisAmount is { } costBasis)
            return CalculateCore(new(costBasis, inputMode, targetMarginPercent,
                salePrice, roundingIncrement, roundingMode, salesTaxRate));

        if (inputMode != PriceInputModes.SalePrice || salePrice is null)
            throw new PricingValidationException(
                "Un producto sin costo base debe tener un precio de venta definido directamente.");
        if (salePrice <= 0)
            throw new PricingValidationException("El precio de venta debe ser mayor que cero.");
        if (!PricingRoundingModes.IsSupported(roundingMode) || roundingIncrement <= 0)
            throw new PricingValidationException("La configuración de redondeo no es válida.");

        var rounded = PriceMargin.RoundPrice(salePrice.Value, roundingIncrement, roundingMode);
        return new(0m, PriceInputModes.SalePrice, null, salePrice.Value, rounded,
            null, roundingIncrement, roundingMode);
    }
    private async Task<PriceProposalSource> RequiredProposalAsync(PricingUserIdentity user, Guid proposalId, CancellationToken ct) =>
        await store.GetProposalAsync(user, proposalId, ct) ?? throw new PricingNotFoundException("No se encontró la propuesta de precio.");

    private static void EnsureReviewable(PriceProposalSource source)
    {
        if (source.Status is not ("PendingReview" or "Approved"))
            throw new PricingConflictException("Solo se pueden modificar propuestas pendientes o aprobadas.");
    }

    private static byte[] DecodeToken(string token)
    {
        try
        {
            var value = Convert.FromBase64String(token);
            return value.Length == 8 ? value : throw new FormatException();
        }
        catch (FormatException) { throw new PricingValidationException("La versión de edición no es válida. Vuelve a abrir el documento antes de continuar."); }
    }

    private static string? Normalize(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum) throw new PricingValidationException($"El valor admite máximo {maximum} caracteres.");
        return normalized;
    }

    private static void Require(PricingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new PricingForbiddenException($"No tienes permiso para realizar esta acción.");
    }
}

public sealed class PricingForbiddenException(string message) : Exception(message);
public sealed class PricingConflictException(string message) : Exception(message);
public sealed class PricingNotFoundException(string message) : Exception(message);
public sealed class PricingValidationException : Exception
{
    public PricingValidationException(string message) : base(message) { }
    public PricingValidationException(string message, Exception inner) : base(message, inner) { }
}
