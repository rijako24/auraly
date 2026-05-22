using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Calcula el precio total de servicios y add-ons desde el catálogo,
/// incluyendo anticipo según BookingPolicy del negocio.
/// El LLM NUNCA debe inventar precios — siempre llama esta tool.
/// </summary>
public sealed class ResolvePricingTool : IAgentTool
{
    private readonly IReservationCheckoutPricing _checkoutPricing;
    private readonly IAddOnCatalogService _addOnCatalog;

    public ResolvePricingTool(
        IReservationCheckoutPricing checkoutPricing,
        IAddOnCatalogService addOnCatalog)
    {
        _checkoutPricing = checkoutPricing;
        _addOnCatalog = addOnCatalog;
    }

    public string Name => "resolve_pricing";

    public string Description =>
        "Calculates service total, add-on totals, and deposit amount from catalog data and booking policy.";

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
                    validation.Hint);
            }

            addOns = validation.NormalizedCsv;
        }

        var result = await _checkoutPricing.ResolveAsync(
            ctx.BusinessId, service, addOns, cancellationToken);

        if (result is null)
            return ToolResultHelper.Error("service_not_found",
                $"Service '{service}' was not found in the catalog.",
                "Call get_service_catalog to get the current list of services.");

        return ToolResultHelper.Ok(new
        {
            total = result.Pricing.TotalDisplay,
            total_cents = result.TotalCents,
            deposit_required = result.DepositRequired,
            deposit_cents = result.DepositCents,
            currency = result.Policy.Currency,
            line_items = result.Pricing.LineItems.Select(li => new { name = li.Name, price = li.Price })
        });
    }
}
