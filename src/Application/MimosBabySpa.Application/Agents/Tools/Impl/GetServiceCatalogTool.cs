using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Retorna el catálogo actualizado de servicios y precios del negocio.
/// La elegibilidad (edad, capacidad, etc.) se infiere del texto en Description + contexto del cliente.
/// </summary>
public sealed class GetServiceCatalogTool : IAgentTool
{
    private readonly ICatalogContentGenerator _catalog;

    public GetServiceCatalogTool(ICatalogContentGenerator catalog) => _catalog = catalog;

    public string Name => "get_service_catalog";

    public string Description =>
        "Returns the current list of services, compatible add-ons per service, and prices from the catalog. " +
        "Call when the customer asks about services or prices, or before resolving pricing. " +
        "Never invent services — show only what this tool returns.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {}
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var content = await _catalog.GenerateAsync(ctx.BusinessId, cancellationToken);
        return ToolResultHelper.Ok(new { catalog = content });
    }
}
