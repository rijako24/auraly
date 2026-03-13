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
    /// Mensaje de diagnóstico (para debugging)
    /// </summary>
    public string DiagnosticMessage { get; set; } = string.Empty;

    /// <summary>
    /// Porcentaje de completitud (0-100)
    /// </summary>
    public int CompletenessPercentage { get; set; }
}
