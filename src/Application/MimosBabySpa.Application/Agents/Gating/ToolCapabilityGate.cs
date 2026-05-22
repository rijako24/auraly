using System.Text.Json;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Gating;

public sealed record GateResult(bool IsAllowed, string? Code, string? Reason, string? Remediation);

public interface IToolCapabilityGate
{
    Task<GateResult> EvaluateAsync(
        IAgentTool tool,
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken ct);
}

public sealed class ToolCapabilityGate : IToolCapabilityGate
{
    private readonly IToolPreconditionProvider _preconditions;
    private readonly IConversationVerificationService _verifications;

    public ToolCapabilityGate(
        IToolPreconditionProvider preconditions,
        IConversationVerificationService verifications)
    {
        _preconditions = preconditions;
        _verifications = verifications;
    }

    public async Task<GateResult> EvaluateAsync(
        IAgentTool tool,
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken ct)
    {
        foreach (var precondition in _preconditions.GetFor(tool.Name))
        {
            var scopeKey = precondition.ScopeKeyResolver(arguments, ctx);
            if (string.IsNullOrWhiteSpace(scopeKey))
            {
                return new GateResult(
                    false,
                    "precondition_incomplete",
                    $"Cannot evaluate '{precondition.FactType}' — required booking fields are missing.",
                    precondition.Remediation);
            }

            var isActive = await _verifications.IsActiveAsync(
                ctx.ConversationId,
                precondition.FactType,
                scopeKey,
                ct);

            if (!isActive)
            {
                return new GateResult(
                    false,
                    "precondition_failed",
                    precondition.MissingMessage,
                    precondition.Remediation);
            }
        }

        return new GateResult(true, null, null, null);
    }
}
