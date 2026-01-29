using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.StateManagement;

/// <summary>
/// Gestor del estado de conversación.
/// Responsable de cargar, guardar y mantener la integridad del ConversationState.
/// 
/// PRINCIPIOS:
/// - Es la ÚNICA interfaz para acceder al estado
/// - Garantiza atomicidad en las actualizaciones
/// - Maneja versionado y optimistic locking
/// - Proporciona auditoría completa
/// </summary>
public interface IConversationStateManager
{
    /// <summary>
    /// Obtiene o crea un estado de conversación para una conversación específica
    /// </summary>
    /// <param name="conversationId">ID único de la conversación</param>
    /// <param name="businessId">ID del negocio</param>
    /// <param name="phone">Teléfono del cliente</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Estado de conversación actual o nuevo</returns>
    Task<ConversationState> GetOrCreateStateAsync(
        Guid conversationId,
        Guid businessId,
        string phone,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda el estado de conversación con control de versiones
    /// </summary>
    /// <param name="conversationId">ID único de la conversación</param>
    /// <param name="state">Estado a guardar</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Estado guardado con versión actualizada</returns>
    Task<ConversationState> SaveStateAsync(
        Guid conversationId,
        ConversationState state,
        CancellationToken cancellationToken = default);

}
