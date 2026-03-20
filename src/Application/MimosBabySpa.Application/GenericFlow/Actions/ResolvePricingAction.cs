using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Resuelve precio del servicio principal, detalle de extras y total desde el catálogo del negocio.
/// Todo el formateo de montos vive en <see cref="ReservationPricingResolver"/>; este action solo mapea outputs.
///
/// Input keys (input_mapping):
///   item             — nombre del servicio ({{variables.service}})
///   selected_add_ons — CSV de extras o vacío / "ninguno"
///
/// Input keys (node config block):
///   pricing — objeto JSON opcional:
///     anticipoPercentage: int — porcentaje de anticipo (1-100). Si está presente y > 0,
///                               <see cref="ReservationPricingResolver"/> emite <c>AnticipoDisplay</c>
///                               y el action lo expone como "anticipo_amount".
///
/// Output keys:
///   service_price         — desde resolver
///   addons_detail         — desde resolver
///   total_price           — desde resolver
///   total_price_invariant — desde resolver
///   anticipo_amount       — solo si el nodo define anticipoPercentage &gt; 0
/// </summary>
public class ResolvePricingAction : IFlowAction
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ReservationPricingResolver _pricingResolver;
    private readonly ILogger<ResolvePricingAction> _logger;

    public string ActionType => "resolve_pricing";

    public ResolvePricingAction(
        ReservationPricingResolver pricingResolver,
        ILogger<ResolvePricingAction> logger)
    {
        _pricingResolver = pricingResolver;
        _logger = logger;
    }

    public async Task<FlowActionResult> ExecuteAsync(
        Dictionary<string, object?> inputs,
        FlowTurnContext ctx,
        CancellationToken ct)
    {
        var item = inputs.GetString("item");
        var addOns = inputs.GetString("selected_add_ons");
        var pricingConfig = ParsePricingConfig(inputs.GetString("pricing"));
        var anticipoPct = pricingConfig?.AnticipoPercentage is > 0 and <= 100
            ? pricingConfig.AnticipoPercentage
            : (int?)null;

        var resolved = await _pricingResolver.ResolveAsync(
            ctx.BusinessId, item, addOns, anticipoPct, ct);
        if (resolved == null)
            return FlowActionResult.Failed("Could not resolve service pricing");

        _logger.LogDebug(
            "ResolvePricing: business={BusinessId} total={Total}",
            ctx.BusinessId, resolved.Total);

        var outputs = new Dictionary<string, object?>
        {
            ["service_price"] = resolved.ServicePriceDisplay,
            ["addons_detail"] = resolved.AddOnsDetailDisplay,
            ["total_price"] = resolved.TotalDisplay,
            ["total_price_invariant"] = resolved.TotalInvariant
        };

        if (resolved.AnticipoDisplay != null)
            outputs["anticipo_amount"] = resolved.AnticipoDisplay;

        return FlowActionResult.Succeeded(outputs);
    }

    private NodePricingConfig? ParsePricingConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<NodePricingConfig>(json, _jsonOpts); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ResolvePricing: failed to parse 'pricing' config block");
            return null;
        }
    }

    private sealed class NodePricingConfig
    {
        public int AnticipoPercentage { get; set; }
    }
}
