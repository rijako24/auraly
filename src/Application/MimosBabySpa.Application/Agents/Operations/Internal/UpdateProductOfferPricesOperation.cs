using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Operations.Internal;

/// <summary>
/// Applies a human price list conservatively. Existing products may gain new
/// capacities or variants, but unknown products are reported instead of created.
/// </summary>
public sealed partial class UpdateProductOfferPricesOperation : IAgentOperation
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProductOfferPricesOperation(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public OperationDescriptor Descriptor { get; } = new(
        "internal.update_product_offer_prices",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "price_list_text": { "type": "string", "minLength": 3 },
            "source": { "type": ["string", "null"], "maxLength": 1000 }
          },
          "required": ["price_list_text"]
        }
        """,
        ["prices.updated", "prices.no_changes", "prices.review_required"],
        ["catalog.prices"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (!OperationJsonHelper.TryGetString(input, "price_list_text", out var text))
            return OperationOutcome.Fail("prices.review_required", "La lista de precios esta vacia.");

        OperationJsonHelper.TryGetString(input, "source", out var source);
        var rows = Parse(text);
        var updated = new List<object>();
        var rejected = new List<object>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var rowKey = $"{row.Model}|{row.Condition}|{row.StorageGb}|{Normalize(row.VariantLabel ?? string.Empty)}";
            if (!seenKeys.Add(rowKey))
            {
                rejected.Add(new { row.Line, reason = "duplicate_row" });
                continue;
            }

            var candidates = await _unitOfWork.Products.SearchOffersAsync(
                context.BusinessId,
                row.Model,
                row.Condition,
                cancellationToken);
            var products = candidates
                .Where(offer => Normalize(offer.Product.Name) == Normalize(row.Model))
                .Select(offer => offer.Product)
                .DistinctBy(value => value.ProductId)
                .ToList();
            if (products.Count != 1)
            {
                rejected.Add(new
                {
                    row.Line,
                    reason = products.Count == 0 ? "product_not_found" : "ambiguous_product"
                });
                continue;
            }

            var product = products[0];
            var exact = candidates
                .Where(offer => offer.ProductId == product.ProductId)
                .Where(offer => !row.StorageGb.HasValue || offer.StorageGb == row.StorageGb)
                .Where(offer => Normalize(offer.VariantLabel ?? string.Empty)
                                == Normalize(row.VariantLabel ?? string.Empty))
                .ToList();
            if (exact.Count > 1)
            {
                rejected.Add(new { row.Line, reason = "ambiguous_offer" });
                continue;
            }

            ProductOffer offer;
            if (exact.Count == 1)
            {
                offer = await _unitOfWork.Products.GetOfferByIdAsync(
                            context.BusinessId,
                            exact[0].ProductOfferId,
                            cancellationToken)
                        ?? exact[0];
            }
            else
            {
                offer = new ProductOffer
                {
                    ProductOfferId = Guid.NewGuid(),
                    ProductId = product.ProductId,
                    BusinessId = context.BusinessId,
                    Condition = row.Condition,
                    StorageGb = row.StorageGb,
                    VariantLabel = row.VariantLabel,
                    Product = product
                };
                await _unitOfWork.Products.CreateOfferAsync(offer, cancellationToken);
            }

            offer.UnitPrice = row.Price;
            offer.Currency = "COP";
            offer.PriceObservedAtUtc = DateTime.UtcNow;
            offer.PriceSourceUrl = Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
                                   && sourceUri.Scheme == Uri.UriSchemeHttps
                ? source
                : offer.PriceSourceUrl;
            offer.IsAvailable = true;
            offer.IsActive = true;
            offer.UpdatedAt = DateTime.UtcNow;

            if (exact.Count == 1)
                await _unitOfWork.Products.UpdateOfferAsync(offer, cancellationToken);
            updated.Add(new
            {
                product = product.Name,
                condition = offer.Condition,
                storage_gb = offer.StorageGb,
                variant = offer.VariantLabel,
                unit_price = offer.UnitPrice,
                currency = offer.Currency
            });
        }

        if (updated.Count > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        var payload = new
        {
            parsed_rows = rows.Count,
            updated_count = updated.Count,
            rejected_count = rejected.Count,
            updated,
            rejected
        };
        var outcome = updated.Count == 0
            ? "prices.no_changes"
            : rejected.Count == 0 ? "prices.updated" : "prices.review_required";
        return OperationOutcome.Ok(outcome, payload);
    }

    internal static IReadOnlyList<PriceRow> Parse(string text)
    {
        var result = new List<PriceRow>();
        string? sectionCondition = null;

        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var normalized = Normalize(line);
            if (normalized.Contains("iphone nuevos", StringComparison.Ordinal)
                || normalized is "nuevos" or "nuevo" or "equipos nuevos")
            {
                sectionCondition = "new";
                continue;
            }
            if (normalized.Contains("iphone usados", StringComparison.Ordinal)
                || normalized is "usados" or "usado" or "equipos usados")
            {
                sectionCondition = "used";
                continue;
            }

            var modelMatch = IphoneModelRegex().Match(line);
            var priceMatches = PriceRegex().Matches(line);
            if (!modelMatch.Success || priceMatches.Count == 0)
                continue;

            var condition = ConditionRegex().Match(line) is { Success: true } conditionMatch
                ? Normalize(conditionMatch.Value).StartsWith("nuev", StringComparison.Ordinal) ? "new" : "used"
                : sectionCondition;
            if (condition is null)
                continue;

            decimal? price = null;
            Match? selectedPriceMatch = null;
            for (var index = priceMatches.Count - 1; index >= 0 && price is null; index--)
            {
                price = ParsePrice(priceMatches[index].Value);
                if (price.HasValue)
                    selectedPriceMatch = priceMatches[index];
            }
            if (!price.HasValue)
                continue;

            int? storageGb = null;
            var storageMatch = StorageRegex().Match(line);
            if (storageMatch.Success
                && int.TryParse(storageMatch.Groups["amount"].Value, out var amount))
            {
                storageGb = storageMatch.Groups["unit"].Value.Equals(
                    "TB", StringComparison.OrdinalIgnoreCase)
                    ? amount * 1024
                    : amount;
            }

            var variantLabel = selectedPriceMatch is null
                ? null
                : CleanVariantLabel(line[(selectedPriceMatch.Index + selectedPriceMatch.Length)..]);
            result.Add(new PriceRow(
                CanonicalModel(modelMatch.Value),
                condition,
                storageGb,
                variantLabel,
                price.Value,
                line));
        }

        return result;
    }

    private static decimal? ParsePrice(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var price)
               && price >= 100_000
            ? price
            : null;
    }

    private static string? CleanVariantLabel(string value)
    {
        var cleaned = Regex.Replace(value, @"[*_🔥👇🏻🇨🇴]+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', ',', '.');
        return string.IsNullOrWhiteSpace(cleaned)
            ? null
            : cleaned[..Math.Min(cleaned.Length, 250)];
    }

    private static string CanonicalModel(string value)
    {
        var words = Regex.Replace(value.Trim(), @"\s+", " ").Split(' ');
        return string.Join(' ', words.Select(word => word.ToLowerInvariant() switch
        {
            "iphone" => "iPhone",
            "pro" => "Pro",
            "max" => "Max",
            "plus" => "Plus",
            "air" => "Air",
            "mini" => "mini",
            _ => word.ToUpperInvariant().EndsWith('E')
                ? word[..^1] + "e"
                : word
        }));
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    [GeneratedRegex(
        @"\biPhone\s+(?:(?:1[1-7])e?|Air)(?:\s+(?:Pro\s+Max|Pro|Plus|mini))?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IphoneModelRegex();

    [GeneratedRegex(@"\b(?:nuevo(?:s)?|usado(?:s)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionRegex();

    [GeneratedRegex(@"(?<amount>\d{1,4})\s*(?<unit>GB|TB)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StorageRegex();

    [GeneratedRegex(@"(?:COP\s*|\$\s*)?\d{1,3}(?:['.,\s]\d{3}){1,2}|\b\d{6,8}\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PriceRegex();

    internal sealed record PriceRow(
        string Model,
        string Condition,
        int? StorageGb,
        string? VariantLabel,
        decimal Price,
        string Line);
}
