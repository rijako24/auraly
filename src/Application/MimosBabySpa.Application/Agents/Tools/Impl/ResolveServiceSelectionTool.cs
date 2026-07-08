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
    private readonly IAddOnCatalogService _addOnCatalog;

    public ResolveServiceSelectionTool(
        ServiceSelectionResolver resolver,
        IConversationFactsService factsService,
        IAddOnCatalogService addOnCatalog)
    {
        _resolver = resolver;
        _factsService = factsService;
        _addOnCatalog = addOnCatalog;
    }

    public string Name => "resolve_service_selection";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.FactWrite];

    public string Description =>
        "Resolves a customer service selection against the active catalog and stores booking.service only for a new/current booking request. " +
        "It does not apply service changes to an existing reservation.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "description": "Customer wording for the service selection."
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

        var roleIndex = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var key = roleIndex.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        ctx.Facts.TryGetValue(key, out var currentService);

        var resolution = await _resolver.ResolveAsync(ctx.BusinessId, text, ct: cancellationToken);
        if (!string.IsNullOrWhiteSpace(currentService)
            && resolution.Status == ServiceSelectionStatus.Resolved
            && !string.IsNullOrWhiteSpace(resolution.ServiceName)
            && currentService.Trim().Equals(resolution.ServiceName.Trim(), StringComparison.OrdinalIgnoreCase))
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

        if (!string.IsNullOrWhiteSpace(currentService))
        {
            var addOnValidation = await _addOnCatalog.ValidateAsync(ctx.BusinessId, currentService, text, cancellationToken);
            if (addOnValidation.IsValid && !string.IsNullOrWhiteSpace(addOnValidation.NormalizedCsv))
            {
                return ToolResultHelper.ErrorWithLlm(
                    "add_on_selection_detected",
                    "Selection matches a compatible add-on for the current service, not a service change.",
                    null,
                    new
                    {
                        next_action = "set_fact",
                        key = ConversationFactKeys.AddOns,
                        value = addOnValidation.NormalizedCsv
                    },
                    recoverable: true);
            }
        }
        if (resolution.Status != ServiceSelectionStatus.Resolved || string.IsNullOrWhiteSpace(resolution.ServiceName))
            return ServiceSelectionToolResults.Unresolved(resolution, text.Trim());

        var schemaEntry = roleIndex.EntryFor(key);

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
}

