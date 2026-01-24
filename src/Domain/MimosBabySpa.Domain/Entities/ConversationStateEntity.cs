namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Entidad EF Core para almacenar el estado de conversación como JSON.
/// Una fila por conversación activa.
/// </summary>
public class ConversationStateEntity
{
    /// <summary>
    /// ID de la conversación (PK, FK a Conversations).
    /// </summary>
    public Guid ConversationId { get; set; }

    /// <summary>
    /// ID del negocio (para multi-tenancy).
    /// </summary>
    public Guid BusinessId { get; set; }

    /// <summary>
    /// Estado serializado como JSON.
    /// </summary>
    public string StateJson { get; set; } = string.Empty;

    /// <summary>
    /// Versión del estado (para optimistic concurrency).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Última actualización.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Fecha de creación.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Conversation Conversation { get; set; } = null!;
    public virtual Business Business { get; set; } = null!;
}
