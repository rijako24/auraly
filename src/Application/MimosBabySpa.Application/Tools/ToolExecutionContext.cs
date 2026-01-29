using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.FlowEngine;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Contexto de ejecución de herramientas
/// </summary>
public class ToolExecutionContext
{
    /// <summary>
    /// ID único de la conversación (para persistencia)
    /// </summary>
    public required Guid ConversationId { get; init; }

    /// <summary>
    /// ID del negocio
    /// </summary>
    public required Guid BusinessId { get; init; }

    /// <summary>
    /// Estado actual de la conversación (mutable)
    /// </summary>
    public required ConversationState State { get; init; }

    /// <summary>
    /// Configuración de campos requeridos para este negocio
    /// </summary>
    public required RequiredFieldsConfiguration RequiredFields { get; init; }

    /// <summary>
    /// Mensaje actual del usuario (para contexto)
    /// </summary>
    public string? UserMessage { get; init; }
}
