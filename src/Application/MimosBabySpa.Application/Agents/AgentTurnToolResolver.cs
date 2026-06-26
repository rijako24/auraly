using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Time;

namespace MimosBabySpa.Application.Agents;

public interface IAgentTurnToolResolver
{
    Task<AgentTurnToolSet> ResolveAsync(
        AgentConfig config,
        BusinessClockSnapshot clockSnapshot,
        CancellationToken ct = default);
}

public sealed record AgentTurnToolSet(
    IReadOnlyList<IAgentTool> ConfiguredTools,
    IReadOnlyList<IAgentTool> EffectiveTools,
    OperatingHoursTurnContext OperatingHours);

public sealed class AgentTurnToolResolver : IAgentTurnToolResolver
{
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IOperatingHoursTurnPolicy _operatingHoursPolicy;

    public AgentTurnToolResolver(
        AgentToolRegistry toolRegistry,
        IOperatingHoursTurnPolicy operatingHoursPolicy)
    {
        _toolRegistry = toolRegistry;
        _operatingHoursPolicy = operatingHoursPolicy;
    }

    public async Task<AgentTurnToolSet> ResolveAsync(
        AgentConfig config,
        BusinessClockSnapshot clockSnapshot,
        CancellationToken ct = default)
    {
        var configuredTools = _toolRegistry.GetToolsForAgent(config.EnabledToolNames);
        var policyResult = await _operatingHoursPolicy.EvaluateAsync(
            config,
            clockSnapshot,
            configuredTools,
            ct);

        return new AgentTurnToolSet(
            configuredTools,
            policyResult.EffectiveTools,
            policyResult.Context);
    }
}