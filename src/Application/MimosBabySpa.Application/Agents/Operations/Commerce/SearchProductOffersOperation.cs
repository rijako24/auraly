using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Operations.Commerce;

public sealed class SearchProductOffersOperation : IAgentOperation
{
    private readonly IUnitOfWork _unitOfWork;

    public SearchProductOffersOperation(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    private const string InputSchema = """
        {
          "type": "object",
          "properties": {
            "product_query": { "type": "string", "minLength": 1 },
            "condition": { "type": "string", "enum": ["new", "used", "refurbished"] }
          },
          "required": ["product_query", "condition"],
          "additionalProperties": false
        }
        """;

    public OperationDescriptor Descriptor { get; } = new(
        "commerce.search_product_offers",
        InputSchema,
        ["offers.found", "offers.not_found"],
        [],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (!OperationJsonHelper.TryGetString(input, "product_query", out var productQuery)
            || !OperationJsonHelper.TryGetString(input, "condition", out var condition))
            return OperationOutcome.Fail(
                "offers.not_found",
                "Product query and condition are required.",
                true);

        condition = condition.Trim().ToLowerInvariant();
        if (condition is not ("new" or "used" or "refurbished"))
            return OperationOutcome.Fail("offers.not_found", "Unsupported product condition.", true);

        var offers = await _unitOfWork.Products.SearchOffersAsync(
            context.BusinessId,
            productQuery.Trim(),
            condition,
            cancellationToken);

        if (offers.Count == 0)
        {
            return OperationOutcome.Ok("offers.not_found", new
            {
                product_query = productQuery.Trim(),
                condition,
                response_guidance =
                    "Indica que no hay una oferta vigente para ese modelo y condicion. No inventes precio, imagen ni disponibilidad."
            });
        }

        var exact = offers
            .Where(value => value.Product.Name.Equals(productQuery.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var selected = (exact.Count > 0 ? exact : offers).Take(5).ToList();
        var effects = BuildMediaEffects(selected);

        return OperationOutcome.Ok(
            "offers.found",
            new
            {
                product_query = productQuery.Trim(),
                condition,
                offers = selected.Select(value => new
                {
                    product_name = value.Product.Name,
                    description = value.Product.Description,
                    condition = value.Condition,
                    storage_gb = value.StorageGb,
                    color = value.Color,
                    variant = value.VariantLabel,
                    unit_price = value.UnitPrice,
                    currency = value.Currency,
                    minimum_battery_health_percent = value.MinimumBatteryHealthPercent,
                    price_observed_at_utc = value.PriceObservedAtUtc,
                    image_url = PrimaryImage(value)?.MediaUrl
                }),
                available_colors = selected
                    .Select(value => value.Color)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                response_guidance =
                    "Presenta solo las ofertas devueltas y sus atributos autoritativos. No inventes politicas, umbrales, garantias, disponibilidad ni atributos ausentes. La imagen se envia por separado."
            },
            effects: effects);
    }

    private static IReadOnlyList<OperationEffect> BuildMediaEffects(IReadOnlyList<ProductOffer> offers)
    {
        return offers
            .GroupBy(offer => offer.ProductId)
            .Select(group => group
                .Select(PrimaryImage)
                .Where(image => image is not null)
                .OrderByDescending(image => image!.IsPrimary)
                .ThenBy(image => image!.DisplayOrder)
                .FirstOrDefault())
            .Where(image => image is not null)
            .Select(image => (OperationEffect)new OutboundMediaOperationEffect(
                image!.MediaUrl,
                "image",
                image.AltText))
            .ToList();
    }

    private static ProductImage? PrimaryImage(ProductOffer offer) =>
        offer.Images
            .Where(value => value.IsActive)
            .OrderByDescending(value => value.IsPrimary)
            .ThenBy(value => value.DisplayOrder)
            .FirstOrDefault()
        ?? offer.Product.Images
            .Where(value => value.IsActive && value.ProductOfferId is null)
            .OrderByDescending(value => value.IsPrimary)
            .ThenBy(value => value.DisplayOrder)
            .FirstOrDefault();
}
