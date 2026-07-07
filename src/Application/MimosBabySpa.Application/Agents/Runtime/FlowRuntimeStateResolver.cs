namespace MimosBabySpa.Application.Agents.Runtime;

public sealed class FlowRuntimeStateResolver : IFlowRuntimeStateResolver
{
    public FlowRuntimeState Resolve(AgentConfig config, AgentToolContext session) => FlowRuntimeState.Default;
}
