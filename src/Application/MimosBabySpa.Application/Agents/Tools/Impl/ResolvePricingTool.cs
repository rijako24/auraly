using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Calculates service and add-on totals from tenant catalog data.
/// Payment rules are resolved by prepare_checkout from the agent checkout configuration.
/// </summary>
public sealed class ResolvePricingTool : IAgentTool
{
    private readonly ReservationPricingResolver _pricing;
    private readonly IAddOnCatalogService _addOnCatalog;

    public ResolvePricingTool(
        ReservationPricingResolver pricing,
        IAddOnCatalogService addOnCatalog)
    {
        _pricing = pricing;
        _addOnCatalog = addOnCatalog;
    }

    public string Name => "resolve_pricing";

    public string Description =>
        "Calculates service total and add-on totals from catalog data.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": {
              "type": "string",
              "description": "Main service name"
            },
            "add_ons": {
              "type": "string",
              "description": "Comma-separated list of add-on names (optional)"
            }
          },
          "required": ["service"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "service", out var service))
            return ToolResultHelper.Error("invalid_args", "Parameter 'service' is required.");

        ToolResultHelper.TryGetString(arguments, "add_ons", out var addOns);
        addOns ??= ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.AddOns);

        if (!string.IsNullOrWhiteSpace(addOns))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                ctx.BusinessId, service, addOns, cancellationToken);

            if (!validation.IsValid)
            {
                return ToolResultHelper.Error(
                    "invalid_add_ons",
                    validation.ErrorMessage ?? "Invalid add-on selection.",
                    validation.Remediation);
            }

            addOns = validation.NormalizedCsv;
        }

        var result = await _pricing.ResolveAsync(
            ctx.BusinessId,
            BuildPricingItems(service, addOns),
            cancellationToken);

        if (result is null)
            return ToolResultHelper.Error("service_not_found",
                $"Service '{service}' was not found in the catalog.",
                "Call get_service_catalog to get the current list of services.");

        return ToolResultHelper.Ok(new
        {
            total = result.TotalDisplay,
            total_cents = (long)(result.Total * 100),
            currency = ResolveCurrency(ctx),
            line_items = result.LineItems.Select(li => new
            {
                name = li.Name,
                price = li.Price,
                base_price = li.BasePrice ?? li.Price,
                discount_amount = li.DiscountAmount,
                promotion_name = li.PromotionName,
                promotion_summary = li.PromotionSummary,
                include_in_checkout_total = li.IncludeInCheckoutTotal
            })
        });
    }

    private static string ResolveCurrency(AgentToolContext ctx)
    {
        var currency = ctx.Config?.Checkout?.Currency;
        return string.IsNullOrWhiteSpace(currency) ? "COP" : currency.Trim().ToUpperInvariant();
    }

    private static IReadOnlyDictionary<string, string?> BuildPricingItems(string service, string? addOns)
    {
        var items = new Dictionary<string, string?> { ["service"] = service };
        if (!string.IsNullOrWhiteSpace(addOns))
            items["add_ons"] = addOns;
        return items;
    }
}
