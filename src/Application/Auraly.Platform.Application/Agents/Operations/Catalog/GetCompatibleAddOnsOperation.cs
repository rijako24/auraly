using System.Text.Json;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Agents.Operations.Catalog;

public sealed class GetCompatibleAddOnsOperation : IAgentOperation
{
    public const string OperationId = "catalog.get_compatible_add_ons";
    private readonly IAddOnCatalogService _catalog;

    public GetCompatibleAddOnsOperation(IAddOnCatalogService catalog) => _catalog = catalog;

    public OperationDescriptor Descriptor { get; } = new(
        OperationId,
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": { "service": { "type": "string" } },
          "required": ["service"]
        }
        """,
        ["catalog.add_ons_available", "catalog.no_add_ons", "input.invalid"],
        ["catalog.read"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default)
    {
        var service = input.TryGetProperty("service", out var serviceElement) && serviceElement.ValueKind == JsonValueKind.String
            ? serviceElement.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(service))
            return OperationOutcome.Fail("input.invalid", "service is required.", true);

        var addOns = await _catalog.GetCompatibleAsync(context.BusinessId, service, cancellationToken);
        var data = new
        {
            service,
            count = addOns.Count,
            addOns = addOns.Select(addOn => new
            {
                name = addOn.AddOnName,
                description = addOn.AddOnDescription,
                price = addOn.AddOnPrice,
                includeInCheckoutTotal = addOn.IncludeInCheckoutTotal
            })
        };
        return OperationOutcome.Ok(
            addOns.Count == 0 ? "catalog.no_add_ons" : "catalog.add_ons_available",
            data);
    }
}
