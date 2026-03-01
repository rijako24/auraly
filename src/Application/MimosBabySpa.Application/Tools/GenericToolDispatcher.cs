using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Dispatcher de herramientas usando Factory Pattern.
/// Ejecuta tools de forma type-safe usando enums en lugar de strings.
/// </summary>
public class GenericToolDispatcher
{
    private readonly IToolFactory _toolFactory;
    private readonly ILogger<GenericToolDispatcher> _logger;

    public GenericToolDispatcher(
        IToolFactory toolFactory,
        ILogger<GenericToolDispatcher> logger)
    {
        _toolFactory = toolFactory ?? throw new ArgumentNullException(nameof(toolFactory));
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta una herramienta por tipo (type-safe)
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolType toolType,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Ejecutando tool: {ToolType}", toolType);

            var handler = _toolFactory.GetTool(toolType);
            var result = await handler.ExecuteAsync(context, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Tool ejecutado exitosamente: {ToolType}", toolType);
            }
            else
            {
                _logger.LogWarning(
                    "Tool ejecutado con errores: {ToolType}. Mensaje: {Message}",
                    toolType, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando tool: {ToolType}", toolType);

            return new ToolExecutionResult
            {
                Success = false,
                Message = $"Error interno: {ex.Message}",
                Exception = ex
            };
        }
    }
}
