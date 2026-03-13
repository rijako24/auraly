namespace MimosBabySpa.Application.Orchestration;

/// <summary>
/// Resumen de acciones ejecutadas en el turno actual.
/// Usado por la generación de respuesta para dar instrucciones precisas al LLM.
/// Multitenant-agnostic: no contiene lógica de negocio específica.
/// </summary>
public class TurnActions
{
    public bool CheckAvailabilityExecuted { get; set; }
    public bool CreateReservationExecuted { get; set; }
    public bool CancellationExecuted { get; set; }

    /// <summary>
    /// True cuando el orquestador determinó que el bot DEBE ofrecer add-ons en este turno.
    /// Directiva exclusiva: prioridad sobre instrucciones de stage.
    /// </summary>
    public bool AddOnOfferingRequired { get; set; }
    public string? AvailabilityResultMessage { get; set; }
    public string? ReservationResultMessage { get; set; }

    /// <summary>
    /// True cuando se generó el link de pago en este turno.
    /// </summary>
    public bool PaymentLinkGenerated { get; set; }

    /// <summary>
    /// Mensaje de error si falló la generación del link.
    /// </summary>
    public string? PaymentLinkError { get; set; }

    /// <summary>
    /// True cuando el stage es AwaitingPayment.
    /// Indica al LLM que debe informar que el pago está pendiente.
    /// </summary>
    public bool IsAwaitingPayment { get; set; }

    /// <summary>
    /// True cuando se ejecutó re-agendamiento de reserva.
    /// </summary>
    public bool RescheduleExecuted { get; set; }

    /// <summary>
    /// True cuando se puso la reserva en espera (OnHold).
    /// </summary>
    public bool SuspendExecuted { get; set; }
}
