using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Resuelve precio del servicio principal, detalle de extras y total desde el catálogo del negocio.
///
/// Input keys (input_mapping):
///   item — nombre del servicio ({{variables.service}})
///   selected_add_ons — CSV de extras o vacío / "ninguno"
///
/// Output keys:
///   service_price — texto formateado p. ej. "$125.000"
///   addons_detail — líneas "- Extra: … — $…" o "—"
///   total_price — total formateado p. ej. "$140.000"
///   total_price_invariant — total decimal string (CultureInfo.InvariantCulture) para cálculo de anticipo
/// </summary>
public class ResolvePricingAction : IFlowAction
{
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

        var resolved = await _pricingResolver.ResolveAsync(ctx.BusinessId, item, addOns, ct);
        if (resolved == null)
            return FlowActionResult.Failed("Could not resolve service pricing");

        _logger.LogDebug(
            "ResolvePricing: business={BusinessId} total={Total}",
            ctx.BusinessId, resolved.Total);

        return FlowActionResult.Succeeded(new Dictionary<string, object?>
        {
            ["service_price"] = resolved.ServicePriceDisplay,
            ["addons_detail"] = resolved.AddOnsDetailDisplay,
            ["total_price"] = resolved.TotalDisplay,
            ["total_price_invariant"] = resolved.TotalInvariant
        });
    }
}
