using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Composition;

public sealed record ToolAvailabilityResult(
    bool IsAvailable,
    string? BlockReason,
    string? Remediation);

public interface IGuardEvaluator
{
    ToolAvailabilityResult EvaluateTool(
        IAgentTool tool,
        AgentConfig config,
        AgentToolContext ctx,
        System.Text.Json.JsonElement arguments);
}
