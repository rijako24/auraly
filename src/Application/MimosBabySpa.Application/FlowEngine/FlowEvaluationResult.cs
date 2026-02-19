using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.FlowEngine;

/// <summary>
/// Resultado de la evaluación del Flow Engine
/// </summary>
public class FlowEvaluationResult
{
    /// <summary>
    /// Campos que aún faltan por recolectar
    /// </summary>
    public List<string> MissingFields { get; set; } = new();

    /// <summary>
    /// Indica si se puede verificar disponibilidad
    /// </summary>
    public bool CanCheckAvailability { get; set; }

    /// <summary>
    /// Indica si se puede crear la reserva
    /// </summary>
    public bool CanCreateReservation { get; set; }

    /// <summary>
    /// Etapa actual del flujo
    /// </summary>
    public TransactionStage CurrentStage { get; set; }

    /// <summary>
    /// Siguiente etapa sugerida
    /// </summary>
    public TransactionStage SuggestedNextStage { get; set; }

    /// <summary>
    /// Mensaje de diagnóstico (para debugging)
    /// </summary>
    public string DiagnosticMessage { get; set; } = string.Empty;

    /// <summary>
    /// Indica si todos los datos requeridos están completos
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Porcentaje de completitud (0-100)
    /// </summary>
    public int CompletenessPercentage { get; set; }

    /// <summary>
    /// Verdadero cuando el stage es ConfirmingBooking.
    /// Por invariante del FlowEngine, ConfirmingBooking solo existe cuando todos los campos están completos.
    /// </summary>
    public bool IsReadyForConfirmation =>
        CurrentStage == TransactionStage.ConfirmingBooking;
}
