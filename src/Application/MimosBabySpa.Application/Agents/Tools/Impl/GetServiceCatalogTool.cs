using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Retorna el catalogo actualizado de servicios y precios del negocio.
/// La elegibilidad (edad, capacidad, etc.) se infiere del texto en Description + contexto del cliente.
/// </summary>
public sealed class GetServiceCatalogTool : IAgentTool
{
    private readonly ICatalogContentGenerator _catalog;

    public GetServiceCatalogTool(ICatalogContentGenerator catalog) => _catalog = catalog;

    public string Name => "get_service_catalog";

    public string Description =>
        "Returns the business service catalog for information requests: service categories, services, compatible add-ons per service, prices, durations, options, alternatives, and service details. " +
        "Use view=categories only when the customer has not named any service family, service type, option, or narrowing keyword and should see only the available service categories/options. " +
        "Use view=services to answer catalog, pricing, option, comparison, service-information, or narrowed service-family questions, including broad family words from the customer. " +
        "Pass query using the customer's own service-family words when the user narrows the catalog. Do not invent or hard-code query values. " +
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
              "enum": ["services", "categories"],
              "description": "Use categories for the initial category/options overview; use services for service rows with price, duration, and add-ons."
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
        var view = string.Equals(viewText, "categories", StringComparison.OrdinalIgnoreCase)
            ? CatalogContentView.Categories
            : CatalogContentView.Services;
        if (view == CatalogContentView.Categories && !string.IsNullOrWhiteSpace(query))
            view = CatalogContentView.Services;

        var content = await _catalog.GenerateAsync(ctx.BusinessId, query, view, cancellationToken);
        return ToolResultHelper.Ok(new
        {
            catalog = content,
            query = string.IsNullOrWhiteSpace(query) ? null : query,
            view = view == CatalogContentView.Categories ? "categories" : "services"
        });
    }
}
