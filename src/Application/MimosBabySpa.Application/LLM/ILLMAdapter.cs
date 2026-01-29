using Azure.AI.OpenAI;

namespace MimosBabySpa.Application.LLM;

/// <summary>
/// Adaptador para comunicación con el LLM (Large Language Model).
/// 
/// Esta interfaz AÍSLA completamente la comunicación con el LLM del resto del sistema.
/// Permite cambiar proveedores (OpenAI, Azure OpenAI, Anthropic, etc.) sin afectar la lógica.
/// 
/// RESPONSABILIDADES:
/// - Enviar mensajes al LLM
/// - Parsear respuestas
/// - Manejar function calling
/// - Gestionar tokens y costos
/// - Retry logic y timeouts
/// 
/// NO ES RESPONSABLE DE:
/// - Decidir qué enviar (eso es del Orchestrator)
/// - Ejecutar herramientas (eso es del ToolDispatcher)
/// - Validar lógica de negocio (eso es del BusinessRuleEngine)
/// </summary>
public interface ILLMAdapter
{
    /// <summary>
    /// Envía un mensaje al LLM y obtiene una respuesta
    /// </summary>
    /// <param name="request">Request con mensajes y configuración</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Respuesta del LLM</returns>
    Task<LLMResponse> SendMessageAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un mensaje con JSON Mode forzado (retorna JSON estructurado)
    /// </summary>
    Task<LLMResponse> SendWithJsonModeAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default);
}

