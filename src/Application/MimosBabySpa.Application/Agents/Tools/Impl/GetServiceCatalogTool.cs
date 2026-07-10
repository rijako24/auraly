using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Retorna el catalogo actualizado de servicios y precios del negocio.
/// La elegibilidad (edad, capacidad, etc.) se infiere del texto en Description + contexto del cliente.
/// </summary>
[AgentToolMetadata("get_service_catalog")]
public sealed class GetServiceCatalogTool : IAgentTool
{
    private readonly ICatalogContentGenerator _catalog;

    public GetServiceCatalogTool(ICatalogContentGenerator catalog) => _catalog = catalog;

    public string Name => "get_service_catalog";

    public IReadOnlyList<string> Capabilities => [];

    public string Description =>
        "Returns the business service catalog for information requests: service categories, services, compatible add-ons per service, prices, durations, options, alternatives, and service details. " +
        "Use view=auto when the turn should consult the catalog and let the tool choose category overview vs filtered services from the tenant catalog. " +
        "Use view=categories only when the customer should see the available service categories/options. " +
        "Use view=services only when the caller intentionally needs service rows with price/duration/add-ons. " +
        "Pass query using the customer's own words. Do not invent or hard-code query values. " +
        "It does not select a service or store booking.service.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Optional customer keyword or service family to filter catalog rows."
            },
            "view": {
              "type": "string",
              "enum": ["auto", "services", "categories"],
              "description": "Use auto for discovery; categories for an overview; services for service rows with price, duration, and add-ons."
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
        ToolResultHelper.TryGetString(arguments, "view", out var viewText);
        var view = viewText?.Trim().ToLowerInvariant() switch
        {
            "categories" => CatalogContentView.Categories,
            "services" => CatalogContentView.Services,
            _ => CatalogContentView.Auto
        };

        var requestedView = view == CatalogContentView.Categories
            ? "categories"
            : view == CatalogContentView.Services ? "services" : "auto";

        var content = await _catalog.GenerateAsync(ctx.BusinessId, query, view, cancellationToken);
        return ToolResultHelper.Ok(new
        {
            catalog = content,
            query = string.IsNullOrWhiteSpace(query) ? null : query,
            view = requestedView,
            requested_view = requestedView
        });
    }
}
