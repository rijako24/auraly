namespace MimosBabySpa.Application.Agents.Templates;

public interface IAgentTurnResponseComposer
{
    /// <summary>
    /// Expande tokens de fragmentos en la respuesta del LLM usando plantillas del agente o defaults de tools.
    /// </summary>
    string Compose(
        AgentConfig config,
        IReadOnlyList<Tools.IAgentTool> enabledTools,
        string llmResponse,
        IEnumerable<TurnFragmentEntry> fragments);
}

public sealed record TurnFragmentEntry(string Token, TurnFragment Fragment);
