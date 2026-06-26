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
        var operatingHours = await _operatingHoursPolicy.EvaluateAsync(config, clockSnapshot, ct);
        var effectiveTools = operatingHours.IsEnforced && operatingHours.IsOutsideOperatingHours
            ? []
            : configuredTools;

        return new AgentTurnToolSet(
            configuredTools,
            effectiveTools,
            operatingHours);
    }
}
