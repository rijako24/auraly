namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Tipos de herramientas disponibles en el sistema.
/// Proporciona type-safety en lugar de usar strings.
/// </summary>
public enum ToolType
{
    /// <summary>
    /// Actualiza el estado de la conversación con valores extraídos
    /// </summary>
    UpdateConversationState,
    
    /// <summary>
    /// Verifica disponibilidad consultando al backend
    /// </summary>
    CheckAvailability,
    
    /// <summary>
    /// Crea una reserva en el sistema
    /// </summary>
    CreateReservation
}
