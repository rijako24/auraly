namespace Auraly.Platform.Application.Constants;

/// <summary>
/// Constantes del pipeline de orquestación.
/// Multitenant: los umbrales son genéricos; futuras extensiones pueden cargar valores por negocio desde BD.
/// </summary>
public static class OrchestrationConstants
{
    /// <summary>
    /// Horas de inactividad para considerar retomo de conversación.
    /// Si state.UpdatedAt es más antiguo que este umbral y hay datos transaccionales, se resetea el estado
    /// preservando solo identidad (CustomerName, Phone, Attributes).
    /// </summary>
    public const double ResumptionThresholdHours = 8.0;
}
