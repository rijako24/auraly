namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Resultado discriminado del bucle de Function Calling.
/// Evita el uso de flags booleanas y permite que el orquestador
/// tome decisiones con un switch expression limpio.
/// </summary>
internal sealed class AgentLoopOutcome
{
    public enum OutcomeKind { Completed, AutoEscalate, Failed }

    public OutcomeKind Kind { get; private init; }

    /// <summary>Respuesta final del LLM (solo cuando Kind=Completed).</summary>
    public string? Response { get; private init; }

    /// <summary>Motivo de escalación o fallo.</summary>
    public string? Reason { get; private init; }

    public static AgentLoopOutcome Completed(string response) =>
        new() { Kind = OutcomeKind.Completed, Response = response };

    public static AgentLoopOutcome AutoEscalate(string reason) =>
        new() { Kind = OutcomeKind.AutoEscalate, Reason = reason };

    public static AgentLoopOutcome Failed(string reason) =>
        new() { Kind = OutcomeKind.Failed, Reason = reason };
}
