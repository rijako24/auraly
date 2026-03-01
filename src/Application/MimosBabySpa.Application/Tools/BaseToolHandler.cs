using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Clase base abstracta para todos los tool handlers.
/// Centraliza la lógica de guardado de estado después de cada operación.
/// 
/// BENEFICIOS:
/// - Responsabilidad única: Cada tool guarda su propio estado
/// - Sin código repetitivo: Guardado centralizado
/// - Desacoplamiento: El orquestador no necesita saber cuándo guardar
/// - Fácil de mantener: Nuevas tools automáticamente guardan el estado
/// </summary>
public abstract class BaseToolHandler : IToolHandler
{
    protected readonly IConversationStateManager _stateManager;
    protected readonly ILogger _logger;

    protected BaseToolHandler(
        IConversationStateManager stateManager,
        ILogger logger)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract string FunctionName { get; }

    public abstract FunctionDefinition GetDefinition();

    /// <summary>
    /// Implementación concreta de la lógica del tool.
    /// Las clases derivadas deben implementar este método.
    /// </summary>
    protected abstract Task<ToolExecutionResult> ExecuteCoreAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ejecuta el tool y guarda automáticamente el estado si fue modificado.
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Ejecutar la lógica específica del tool
            var result = await ExecuteCoreAsync(context, cancellationToken);

            // CRÍTICO: Si el estado fue modificado, guardarlo automáticamente en BD
            if (result.Success && result.StateModified)
            {
                _logger.LogInformation(
                    "Tool '{FunctionName}' modificó el estado, guardando en BD...",
                    FunctionName);

                await _stateManager.SaveStateAsync(
                    context.ConversationId,
                    context.State,
                    cancellationToken);

                _logger.LogInformation(
                    "Estado guardado exitosamente después de '{FunctionName}'. Version={Version}",
                    FunctionName, context.State.Version);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error crítico ejecutando tool '{FunctionName}'",
                FunctionName);

            return new ToolExecutionResult
            {
                Success = false,
                Message = $"Error interno en {FunctionName}: {ex.Message}",
                Exception = ex
            };
        }
    }
}
