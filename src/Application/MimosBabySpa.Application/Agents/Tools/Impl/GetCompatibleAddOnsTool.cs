using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Retorna los complementos compatibles con un servicio concreto.
/// Es una consulta estructurada; no modifica facts ni decide la respuesta al cliente.
/// </summary>
public sealed class GetCompatibleAddOnsTool : IAgentTool
{
    private readonly IAddOnCatalogService _addOnCatalog;

    public GetCompatibleAddOnsTool(IAddOnCatalogService addOnCatalog)
    {
        _addOnCatalog = addOnCatalog;
    }

    public string Name => "get_compatible_add_ons";

    public string Description =>
        "Returns the add-ons compatible with the selected service. If count is 0, there are no compatible add-ons to offer.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": {
              "type": "string",
              "description": "Exact selected service name from the catalog. Optional when booking.service fact is already set."
            }
          }
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "service", out var service))
            service = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(service))
            return ToolResultHelper.MissingPrerequisites(["service"]);

        var addOns = await _addOnCatalog.GetCompatibleAsync(
            ctx.BusinessId,
            service,
            cancellationToken);

        return ToolResultHelper.Ok(new
        {
            service,
            count = addOns.Count,
            add_ons = addOns.Select(a => new
            {
                name = a.AddOnName,
                description = a.AddOnDescription,
                price = a.AddOnPrice,
                include_in_checkout_total = a.IncludeInCheckoutTotal
            })
        });
    }
}
