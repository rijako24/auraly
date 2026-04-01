using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Resuelve precios de todos los items recibidos en input_mapping contra el catálogo del negocio.
/// Genérico: no tiene keys hardcodeados. Trata cada input como un item (o CSV de items) a pricear.
/// Para cada input key devuelve "NombreCanónico — $Precio"; el output_mapping del flujo controla
/// dónde se escriben en el estado.
///
/// Output keys fijos:
///   total_price          — total formateado (e.g. "$135,000")
///   total_price_invariant — total como string parseable (InvariantCulture)
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
        var items = inputs
            .Where(kv => kv.Value is string s && !string.IsNullOrWhiteSpace(s))
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value!.ToString());

        var result = await _pricingResolver.ResolveAsync(ctx.BusinessId, items, ct);
        if (result == null)
            return FlowActionResult.Failed("No priceable items found");

        _logger.LogDebug(
            "ResolvePricing: business={BusinessId} items={Count} total={Total}",
            ctx.BusinessId, result.LineItems.Count, result.Total);

        var output = new Dictionary<string, object?>();

        foreach (var (key, formatted) in result.FormattedByKey)
            output[key] = formatted;

        output["total_price"] = result.TotalDisplay;
        output["total_price_invariant"] = result.TotalInvariant;

        return FlowActionResult.Succeeded(output);
    }
}
