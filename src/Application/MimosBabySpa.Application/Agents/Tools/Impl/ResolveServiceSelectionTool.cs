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
        "Resolves the customer's raw service selection against the active service catalog. " +
        "Stores booking.service only when the selection identifies one catalog service unambiguously.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "description": "Raw customer wording for the service selection. Use the customer's own words, not an inferred catalog name."
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
            return ToolResultHelper.Ok(new
            {
                selection_status = FormatStatus(resolution.Status),
                service = (string?)null,
                candidates = resolution.Candidates,
                storage = "none",
                resolution_hint = BuildResolutionHint(resolution)
            });
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

    private static string FormatStatus(ServiceSelectionStatus status) => status switch
    {
        ServiceSelectionStatus.Ambiguous => "ambiguous",
        ServiceSelectionStatus.NotFound => "not_found",
        _ => "resolved"
    };

    private static string BuildResolutionHint(ServiceSelectionResolution resolution) => resolution.Status switch
    {
        ServiceSelectionStatus.Ambiguous when resolution.Candidates.Count > 0 =>
            $"Ask the customer which exact catalog option they prefer: {string.Join(", ", resolution.Candidates)}.",
        ServiceSelectionStatus.NotFound =>
            "Ask the customer to choose one exact service from the catalog.",
        _ => "Continue with the resolved service."
    };
}

