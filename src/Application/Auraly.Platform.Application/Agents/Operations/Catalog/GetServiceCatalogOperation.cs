using System.Text.Json;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Agents.Operations.Catalog;

public sealed class GetServiceCatalogOperation : IAgentOperation
{
    public const string OperationId = "catalog.get_services";
    private readonly ICatalogContentGenerator _catalog;

    public GetServiceCatalogOperation(ICatalogContentGenerator catalog) => _catalog = catalog;

    public OperationDescriptor Descriptor { get; } = new(
        OperationId,
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": { "type": ["string", "null"] },
            "view": { "type": "string", "enum": ["auto", "services", "categories"] }
          },
          "required": ["view"]
        }
        """,
        ["catalog.services_returned"],
        ["catalog.read"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default)
    {
        var query = ReadString(input, "query");
        var viewText = ReadString(input, "view") ?? "auto";
        var view = viewText.ToLowerInvariant() switch
        {
            "categories" => CatalogContentView.Categories,
            "services" => CatalogContentView.Services,
            _ => CatalogContentView.Auto
        };
        var catalog = await _catalog.GenerateAsync(context.BusinessId, query, view, cancellationToken);
        return OperationOutcome.Ok("catalog.services_returned", new
        {
            catalog,
            query,
            view = viewText.ToLowerInvariant()
        });
    }

    private static string? ReadString(JsonElement input, string property) =>
        input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
}
