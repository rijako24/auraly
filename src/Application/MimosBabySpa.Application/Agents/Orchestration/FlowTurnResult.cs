namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// Resultado de una llamada al LLM por el FlowEngine (JSON: intent + reply).
/// Los facts de usuario se persisten solo mediante la tool <c>set_fact</c>.
/// </summary>
public sealed class FlowTurnResult
{
    /// <summary>"Continue" | "Confirm" | "Deny" | "OffTopic" | "Escalate"</summary>
    public string Intent { get; init; } = "Continue";

    public IReadOnlyList<MimosBabySpa.Application.LLM.ToolCallRequest> ToolCalls { get; init; } = [];

    /// <summary>Respuesta al cliente generada por el LLM.</summary>
    public string Reply { get; init; } = string.Empty;

    public int Tokens { get; init; }

    public static FlowTurnResult Fallback(string reply) =>
        new() { Intent = "Continue", Reply = reply };
}
