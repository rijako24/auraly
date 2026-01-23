using MimosBabySpa.Application.Models;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Dispatcher que ejecuta las herramientas solicitadas por OpenAI.
/// Cada herramienta es responsable de validar y completar sus propios parámetros.
/// </summary>
public interface IToolDispatcher
{
    /// <summary>
    /// Ejecuta una herramienta solicitada por OpenAI.
    /// </summary>
    /// <param name="businessId">ID del negocio</param>
    /// <param name="toolCall">Solicitud de ejecución de herramienta</param>
    /// <param name="conversationId">ID de la conversación (opcional, para herramientas que necesitan contexto)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado de la ejecución de la herramienta</returns>
    Task<ToolCallResult> ExecuteToolAsync(
        Guid businessId,
        ToolCallRequest toolCall,
        Guid? conversationId = null,
        CancellationToken cancellationToken = default);
    
    List<ToolDefinition> GetAvailableTools();
}
