using System.Globalization;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve precios de items del catalogo de un negocio.
/// Generico: no distingue entre servicios, add-ons ni complementos.
/// Recibe un diccionario de items, resuelve cada uno contra la BD y suma totales.
/// </summary>
public class ReservationPricingResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _nameResolver;
    private readonly ILogger<ReservationPricingResolver> _logger;
    private readonly IPromotionPricingService _promotions;

    public ReservationPricingResolver(
        IUnitOfWork unitOfWork,
        ServiceNameResolver nameResolver,
        ILogger<ReservationPricingResolver> logger,
        IPromotionPricingService promotions)
    {
        _unitOfWork = unitOfWork;
        _nameResolver = nameResolver;
        _logger = logger;
        _promotions = promotions;
    }

    /// <summary>
    /// Resuelve precios para un conjunto de items identificados por clave.
    /// Cada valor puede ser un nombre simple o CSV (separado por <c>,</c> o <c>;</c>).
    /// Valores vacios, nulos o "ninguno" se ignoran.
    /// </summary>
    public async Task<PricingResult?> ResolveAsync(
        Guid businessId,
        IReadOnlyDictionary<string, string?> items,
        CancellationToken ct = default)
    {
        var rawLineItems = new List<ResolvedServicePrice>();
        var formattedByKey = new Dictionary<string, string>();

        foreach (var (inputKey, rawValue) in items)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || IsSkippable(rawValue))
                continue;

            var resolvedForKey = new List<string>();

            foreach (var name in SplitNames(rawValue))
            {
                var canonical = await _nameResolver.ResolveAsync(businessId, name, ct);
                if (canonical == null)
                {
                    _logger.LogWarning("PricingResolver: '{Name}' not resolved - skipped", name);
                    continue;
                }

                var entity = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, canonical);
                if (entity == null) continue;

                rawLineItems.Add(new ResolvedServicePrice(
                    entity.ServiceId,
                    entity.ServiceName,
                    entity.ServiceCategory?.Name,
                    entity.Price,
                    entity.IncludeInCheckoutTotal));
                resolvedForKey.Add(FormatItem(entity.ServiceName, entity.Price));
            }

            if (resolvedForKey.Count > 0)
                formattedByKey[inputKey] = string.Join("; ", resolvedForKey);
        }

        if (rawLineItems.Count == 0) return null;

        var pricing = await _promotions.EvaluateAsync(
            businessId,
            rawLineItems.Select(item => new PromotionPricingItem(
                item.ServiceId.ToString("N"),
                PromotionItemType.Service,
                null,
                item.ServiceId,
                item.Name,
                item.CategoryName,
                item.Price,
                1,
                item.IncludeInCheckoutTotal)).ToList(),
            ct: ct);

        var lineItems = pricing.Items
            .Select(i => new PricingLineItem(
                i.Item.Name,
                i.EffectiveUnitPrice,
                i.Item.IncludeInTotal,
                i.Item.UnitPrice,
                i.DiscountAmount,
                i.PromotionName,
                i.PromotionSummary))
            .ToList();

        var total = pricing.Items.Where(li => li.Item.IncludeInTotal).Sum(li => li.LineTotal);
        return new PricingResult(lineItems, formattedByKey, total);
    }

    private static string FormatItem(string name, decimal price) =>
        $"{name} - ${price.ToString("N0", CultureInfo.InvariantCulture)}";

    private static bool IsSkippable(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ||
        string.Equals(raw.Trim(), "ninguno", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitNames(string raw)
    {
        foreach (var part in raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.Equals(part, "ninguno", StringComparison.OrdinalIgnoreCase))
                yield return part;
        }
    }

    private sealed record ResolvedServicePrice(
        Guid ServiceId,
        string Name,
        string? CategoryName,
        decimal Price,
        bool IncludeInCheckoutTotal);
}

public sealed record PricingLineItem(
    string Name,
    decimal Price,
    bool IncludeInCheckoutTotal = true,
    decimal? BasePrice = null,
    decimal DiscountAmount = 0,
    string? PromotionName = null,
    string? PromotionSummary = null);

public sealed record PricingResult(
    IReadOnlyList<PricingLineItem> LineItems,
    IReadOnlyDictionary<string, string> FormattedByKey,
    decimal Total)
{
    public string TotalDisplay =>
        $"${Total.ToString("N0", CultureInfo.InvariantCulture)}";

    public string TotalInvariant =>
        Total.ToString(CultureInfo.InvariantCulture);
}
