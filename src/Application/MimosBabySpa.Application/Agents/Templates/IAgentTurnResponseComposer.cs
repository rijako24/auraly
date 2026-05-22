namespace MimosBabySpa.Application.Agents.Templates;

public interface IAgentTurnResponseComposer
{
    /// <summary>
    /// Expande tokens {{CHECKOUT:...}} / {{CONFIRMATION:...}} en la respuesta del LLM
    /// usando plantillas del prompt del agente.
    /// </summary>
    string Compose(string agentSystemPrompt, string llmResponse, IEnumerable<TurnFragmentEntry> fragments);
}

public sealed record TurnFragmentEntry(string Token, TurnFragment Fragment);
