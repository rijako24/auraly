using System.Text.Json;
using Auraly.Platform.Application.Agents.Facts;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Agents.Operations.Catalog;

public sealed class ResolveServiceSelectionOperation : IAgentOperation
{
    public const string OperationId = "catalog.resolve_service";
    private readonly ServiceSelectionResolver _resolver;
    private readonly IAddOnCatalogService _addOns;

    public ResolveServiceSelectionOperation(ServiceSelectionResolver resolver, IAddOnCatalogService addOns)
    {
        _resolver = resolver;
        _addOns = addOns;
    }

    public OperationDescriptor Descriptor { get; } = new(
        OperationId,
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": { "text": { "type": "string" } },
          "required": ["text"]
        }
        """,
        [
            "catalog.service_resolved",
            "catalog.service_unchanged",
            "catalog.add_on_detected",
            "catalog.service_ambiguous",
            "catalog.service_not_found",
            "input.invalid"
        ],
        ["catalog.resolve"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default)
    {
        var text = input.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(text))
            return OperationOutcome.Fail("input.invalid", "text is required.", true);

        var roles = new FactRoleIndex(context.Config.FactSchema);
        var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        context.Facts.TryGetValue(serviceKey, out var currentService);
        var resolution = await _resolver.ResolveAsync(context.BusinessId, text, ct: cancellationToken);

        if (resolution.Status == ServiceSelectionStatus.Resolved
            && !string.IsNullOrWhiteSpace(resolution.ServiceName)
            && resolution.ServiceName.Equals(currentService, StringComparison.OrdinalIgnoreCase))
        {
            return OperationOutcome.Ok("catalog.service_unchanged", new
            {
                service = resolution.ServiceName,
                key = serviceKey
            });
        }

        if (!string.IsNullOrWhiteSpace(currentService))
        {
            var addOn = await _addOns.ValidateAsync(context.BusinessId, currentService, text, cancellationToken);
            if (addOn.IsValid && !string.IsNullOrWhiteSpace(addOn.NormalizedCsv))
            {
                return OperationOutcome.Ok("catalog.add_on_detected", new
                {
                    service = currentService,
                    addOns = addOn.NormalizedCsv
                });
            }
        }

        if (resolution.Status == ServiceSelectionStatus.Resolved && !string.IsNullOrWhiteSpace(resolution.ServiceName))
        {
            return OperationOutcome.Ok("catalog.service_resolved", new
            {
                service = resolution.ServiceName,
                key = serviceKey
            });
        }

        return resolution.Status == ServiceSelectionStatus.Ambiguous
            ? OperationOutcome.Fail(
                "catalog.service_ambiguous",
                "Service selection is ambiguous.",
                true,
                "catalog.service_selection",
                new { query = text, candidates = resolution.Candidates })
            : OperationOutcome.Fail(
                "catalog.service_not_found",
                "Service selection was not found.",
                true,
                "catalog.service_selection",
                new { query = text });
    }
}
