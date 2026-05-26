using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Identity;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Leadgen-pack tool: persiste datos de lead vía set_fact + sync de identidad.
/// </summary>
public sealed class CaptureLeadTool : IAgentTool
{
    private readonly IConversationFactsService _factsService;
    private readonly IIdentityAttributeService _identityAttributes;

    public CaptureLeadTool(
        IConversationFactsService factsService,
        IIdentityAttributeService identityAttributes)
    {
        _factsService = factsService;
        _identityAttributes = identityAttributes;
    }

    public string PackId => Packs.Leadgen.LeadgenPackIds.Leadgen;

    public string Name => "capture_lead";

    public string Description =>
        "Persists lead qualification data as structured facts and syncs persistent identity attributes.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "key": { "type": "string", "description": "Fact key or alias from the tenant schema" },
            "value": { "type": "string", "description": "Structured value" }
          },
          "required": ["key", "value"]
        }
        """;

    public async Task<string> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(invocation.Arguments, "key", out var rawKey)
            || !ToolResultHelper.TryGetString(invocation.Arguments, "value", out var rawValue))
        {
            return ToolResultHelper.MissingPrerequisites(["key", "value"]);
        }

        var ctx = invocation.Context;
        var index = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var key = index.NormalizeKey(rawKey.Trim());

        if (!FactKeyNormalizer.TryNormalizeKey(key, out key))
        {
            return ToolResultHelper.Error(
                "invalid_key",
                "Fact key must be a short snake_case identifier.");
        }

        if (!FactKeyNormalizer.TryNormalizeValue(rawValue, out var value))
        {
            return ToolResultHelper.Error("invalid_value", "Fact value cannot be empty.");
        }

        await _factsService.SetAsync(ctx.ConversationId, ctx.BusinessId, key, value, cancellationToken);
        ctx.Facts[key] = value;

        var schemaEntry = index.EntryFor(key);
        if (schemaEntry is not null)
        {
            await _identityAttributes.SyncFromFactAsync(ctx, schemaEntry, value, cancellationToken);
        }

        return ToolResultHelper.Ok(new { key, value, storage = "fact" });
    }
}
