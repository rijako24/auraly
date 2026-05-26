namespace MimosBabySpa.Application.Agents.Orchestration;

public interface IFlowEngine
{
    Task<AgentTurnResult> ProcessTurnAsync(
        AgentConfig config,
        AgentToolContext session,
        string userMessage,
        CancellationToken ct);
}
