using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("resolve_service_selection", Capabilities = new[] { ToolCapabilities.FactWrite })]
public sealed class ResolveServiceSelectionTool : IAgentTool
{
    private readonly ServiceSelectionResolver _resolver;
    private readonly IConversationFactsService _factsService;

    public ResolveServiceSelectionTool(
        ServiceSelectionResolver resolver,
        IConversationFactsService factsService)
    {
        _resolver = resolver;
        _factsService = factsService;
    }

    public string Name => "resolve_service_selection";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.FactWrite];

    public string Description =>
        "Use this instead of set_fact to store booking.service when the customer selects, confirms, or changes to a service. " +
        "Resolve the customer's raw service wording against the active catalog and store booking.service only when the selection identifies one catalog service unambiguously. " +
        "If the latest user message is elliptical, include immediate conversation context that belongs to the same service request. " +
        "Do not use it for catalog, pricing, option, comparison, or service-information questions.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "description": "Customer wording for the service selection. If the latest message is elliptical, include immediate same-request context already stated by the customer; do not invent a catalog name."
            }
          },
          "required": ["text"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var text = ToolResultHelper.TryGetString(arguments, "text", out var rawText)
            ? rawText
            : ctx.LatestUserMessage;

        if (string.IsNullOrWhiteSpace(text))
            return ToolResultHelper.MissingPrerequisites(["text"]);

        var resolution = await _resolver.ResolveAsync(ctx.BusinessId, text, ct: cancellationToken);
        if (resolution.Status != ServiceSelectionStatus.Resolved || string.IsNullOrWhiteSpace(resolution.ServiceName))
        {
            return ToolResultHelper.Error(
                BuildResolutionErrorCode(resolution),
                BuildResolutionErrorMessage(resolution),
                BuildResolutionHint(resolution),
                recoverable: true);
        }

        var roleIndex = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var key = roleIndex.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        var schemaEntry = roleIndex.EntryFor(key);

        ctx.Facts.TryGetValue(key, out var previousValue);
        if (!string.IsNullOrWhiteSpace(previousValue)
            && previousValue.Trim().Equals(resolution.ServiceName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Ok(new
            {
                selection_status = "resolved",
                service = resolution.ServiceName,
                key,
                unchanged = true,
                storage = "fact_unchanged"
            });
        }

        await _factsService.SetAsync(
            ctx.ConversationId,
            ctx.BusinessId,
            key,
            resolution.ServiceName,
            schemaEntry?.ShouldRememberAcrossRequests() ?? false,
            cancellationToken);

        ctx.Facts[key] = resolution.ServiceName;

        return ToolResultHelper.Ok(new
        {
            selection_status = "resolved",
            service = resolution.ServiceName,
            key,
            storage = "fact"
        });
    }

    private static string BuildResolutionErrorCode(ServiceSelectionResolution resolution) => resolution.Status switch
    {
        ServiceSelectionStatus.Ambiguous => "service_selection_ambiguous",
        ServiceSelectionStatus.NotFound => "service_selection_not_found",
        _ => "service_selection_unresolved"
    };

    private static string BuildResolutionErrorMessage(ServiceSelectionResolution resolution) => resolution.Status switch
    {
        ServiceSelectionStatus.Ambiguous => "Service selection is ambiguous.",
        ServiceSelectionStatus.NotFound => "Service selection was not found.",
        _ => "Service selection could not be resolved."
    };

    private static string BuildResolutionHint(ServiceSelectionResolution resolution) => resolution.Status switch
    {
        ServiceSelectionStatus.Ambiguous or ServiceSelectionStatus.NotFound =>
            "Consult the catalog before answering.",
        _ => "Continue with the resolved service."
    };

}

