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
    public string? AvailabilityResultMessage { get; set; }
    public string? ReservationResultMessage { get; set; }
}
