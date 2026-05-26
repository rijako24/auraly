namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// Abstracción LLM del motor de flujo: JSON (intent + reply) y function calling.
/// Una invocación por iteración del bucle del turno.
/// </summary>
public interface IFlowLlm
{
    Task<FlowTurnResult> RunAsync(FlowLlmRequest request, CancellationToken ct);
}
