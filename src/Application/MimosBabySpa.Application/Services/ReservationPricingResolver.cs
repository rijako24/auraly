using System.Globalization;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve precios de items del catálogo de un negocio (coincidencia exacta de nombre).
/// </summary>
public class ReservationPricingResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReservationPricingResolver> _logger;

    public ReservationPricingResolver(
        IUnitOfWork unitOfWork,
        ILogger<ReservationPricingResolver> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<PricingResult?> ResolveAsync(
        Guid businessId,
        IReadOnlyDictionary<string, string?> items,
        CancellationToken ct = default)
    {
        var lineItems = new List<PricingLineItem>();
        var formattedByKey = new Dictionary<string, string>();
        var activeServices = (await _unitOfWork.Services.GetActiveByBusinessIdAsync(businessId)).ToList();

        foreach (var (inputKey, rawValue) in items)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || IsSkippable(rawValue))
                continue;

            var resolvedForKey = new List<string>();

            foreach (var name in SplitNames(rawValue))
            {
                var canonical = ActiveServiceCatalogMatcher.MatchExact(activeServices, name);
                if (canonical is null)
                {
                    _logger.LogWarning("PricingResolver: '{Name}' not in active catalog — skipped", name);
                    continue;
                }

                var entity = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, canonical);
                if (entity is null)
                    continue;

                lineItems.Add(new PricingLineItem(entity.ServiceName, entity.Price));
                resolvedForKey.Add(FormatItem(entity.ServiceName, entity.Price));
            }

            if (resolvedForKey.Count > 0)
                formattedByKey[inputKey] = string.Join("; ", resolvedForKey);
        }

        if (lineItems.Count == 0)
            return null;

        var total = lineItems.Sum(li => li.Price);
        return new PricingResult(lineItems, formattedByKey, total);
    }

    private static string FormatItem(string name, decimal price) =>
        $"{name} — ${price.ToString("N0", CultureInfo.InvariantCulture)}";

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
}

public sealed record PricingLineItem(string Name, decimal Price);

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
