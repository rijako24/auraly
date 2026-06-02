namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Resultado de procesar un turno del agente.
/// </summary>
public sealed class AgentTurnResult
{
    public bool Success { get; init; }
    public string Response { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    /// <summary>Indica que el agente escaló a un humano en este turno.</summary>
    public bool EscalatedToHuman { get; init; }

    /// <summary>Indica que se creó una reserva en este turno (para actualizar lead).</summary>
    public bool ReservationCreated { get; init; }

    /// <summary>Métricas de tokens del turno para observabilidad.</summary>
    public int TotalTokens { get; init; }

    /// <summary>Número de tool calls ejecutadas en el turno.</summary>
    public int ToolCallCount { get; init; }

    /// <summary>
    /// Mensajes adicionales (texto/adjuntos) a enviar tras la respuesta principal, en orden.
    /// </summary>
    public IReadOnlyList<OutboundMessage> OutboundMessages { get; init; } = [];

    public static AgentTurnResult Ok(
        string response,
        bool escalated = false,
        bool reservationCreated = false,
        int tokens = 0,
        int toolCalls = 0,
        IReadOnlyList<OutboundMessage>? outboundMessages = null) =>
        new()
        {
            Success = true,
            Response = response,
            EscalatedToHuman = escalated,
            ReservationCreated = reservationCreated,
            TotalTokens = tokens,
            ToolCallCount = toolCalls,
            OutboundMessages = outboundMessages ?? []
        };

    public static AgentTurnResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error, Response = string.Empty };
}
