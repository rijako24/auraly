using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Calcula el precio total de servicios y add-ons desde el catálogo.
/// El LLM NUNCA debe inventar precios — siempre llama esta tool.
/// </summary>
public sealed class ResolvePricingTool : IAgentTool
{
    private readonly ReservationPricingResolver _pricing;

    public ResolvePricingTool(ReservationPricingResolver pricing) => _pricing = pricing;

    public string Name => "resolve_pricing";

    public string Description =>
        "Calculates the total price for a service and optional add-ons from the catalog. " +
        "Always call this before presenting prices or generating payment links.";

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

        var items = new Dictionary<string, string?> { ["service"] = service };

        if (ToolResultHelper.TryGetString(arguments, "add_ons", out var addOns) && !string.IsNullOrWhiteSpace(addOns))
            items["add_ons"] = addOns;

        var result = await _pricing.ResolveAsync(ctx.BusinessId, items, cancellationToken);

        if (result is null)
            return ToolResultHelper.Error("service_not_found",
                $"Service '{service}' was not found in the catalog.",
                "Call get_service_catalog to get the current list of services.");

        return ToolResultHelper.Ok(new
        {
            total = result.TotalDisplay,
            total_cents = (long)(result.Total * 100),
            line_items = result.LineItems.Select(li => new { name = li.Name, price = li.Price })
        });
    }
}
