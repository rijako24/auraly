using MimosBabySpa.Application.Models;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Dispatcher que ejecuta las herramientas solicitadas por OpenAI
/// </summary>
public interface IToolDispatcher
{
    Task<ToolCallResult> ExecuteToolAsync(
        Guid businessId,
        ToolCallRequest toolCall,
        CancellationToken cancellationToken = default);
    
    List<ToolDefinition> GetAvailableTools();
}
