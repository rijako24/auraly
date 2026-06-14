namespace MimosBabySpa.Application.Agents.Testing;

public interface IAgentTestRuntimeFactory
{
    IAgentConversationService Create(
        AgentTestExecutionLog log,
        IDictionary<string, string>? initialFacts = null);
}
