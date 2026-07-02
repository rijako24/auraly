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
        "Returns the business service catalog for information requests: services, compatible add-ons per service, prices, durations, options, alternatives, and service details. " +
        "Use it to answer catalog, pricing, option, comparison, or service-information questions. " +
        "Pass query using the customer's own service-family words when the user narrows the catalog. Do not invent or hard-code query values. " +
        "It does not select a service or store booking.service.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Optional customer keyword or service family to filter catalog rows."
            }
          }
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "query", out var query);
        var content = await _catalog.GenerateAsync(ctx.BusinessId, query, cancellationToken);
        return ToolResultHelper.Ok(new { catalog = content, query = string.IsNullOrWhiteSpace(query) ? null : query });
    }
}
