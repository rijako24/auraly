using System.Text;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace MimosBabySpa.Application.Services;

public class ConversationContextService : IConversationContextService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessConfigurationService _businessConfigService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConversationContextService> _logger;

    public ConversationContextService(
        IUnitOfWork unitOfWork,
        IBusinessConfigurationService businessConfigService,
        IServiceProvider serviceProvider,
        ILogger<ConversationContextService> logger)
    {
        _unitOfWork = unitOfWork;
        _businessConfigService = businessConfigService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task AddContextAsync(Guid conversationId, string context)
    {
        try
        {
            // Validar que el contexto no esté vacío
            if (string.IsNullOrWhiteSpace(context))
            {
                _logger.LogDebug("Intento de agregar contexto vacío para conversación {ConversationId}. Se omite.", conversationId);
                return;
            }

            // Normalizar el contexto para comparación
            var normalizedNewContext = context.Trim();
            
            // Verificar si ya existe en la base de datos (consulta directa, sin traer toda la lista)
            var exists = await _unitOfWork.ConversationContexts.ExistsAsync(conversationId, normalizedNewContext);
            
            if (exists)
            {
                _logger.LogDebug("Contexto duplicado detectado para conversación {ConversationId}: {Context}. Se omite.", conversationId, normalizedNewContext);
                return;
            }

            // Si no es duplicado, agregar el nuevo contexto
            await _unitOfWork.ConversationContexts.CreateAsync(conversationId, normalizedNewContext);
            await _unitOfWork.SaveChangesAsync();
            _logger.LogDebug("Contexto agregado para conversación {ConversationId}: {Context}", conversationId, normalizedNewContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al agregar contexto para conversación {ConversationId}", conversationId);
            throw;
        }
    }

    public async Task<List<string>> GetAllContextAsync(Guid conversationId)
    {
        var contexts = await _unitOfWork.ConversationContexts.GetByConversationIdAsync(conversationId);
        return contexts.Select(c => c.Context).Where(c => !string.IsNullOrEmpty(c)).ToList();
    }

    public async Task ClearContextAsync(Guid conversationId)
    {
        try
        {
            await _unitOfWork.ConversationContexts.DeleteByConversationIdAsync(conversationId);
            await _unitOfWork.SaveChangesAsync();
            _logger.LogDebug("Contexto eliminado para conversación {ConversationId}", conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar contexto de conversación {ConversationId}", conversationId);
            throw;
        }
    }

    public async Task<string> BuildContextMessageAsync(Guid conversationId, Guid businessId)
    {
        try
        {
            // Verificar si hay ContextData configurado
            var contextData = await _businessConfigService.GetBusinessConfigurationValueAsync(
                businessId, 
                Domain.Enums.BusinessConfigurationKey.ContextData);
            
            // Si no hay ContextData configurado, no construir contexto
            if (string.IsNullOrEmpty(contextData))
            {
                return string.Empty;
            }

            var allContext = await GetAllContextAsync(conversationId);
            
            // Si no hay contexto en la base de datos, retornar vacío
            if (!allContext.Any())
            {
                return string.Empty;
            }

            // Unir todas las oraciones con saltos de línea
            return string.Join("\n", allContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al construir mensaje de contexto para conversación {ConversationId}", conversationId);
            return string.Empty;
        }
    }

    public async Task<int> AddContextBatchAsync(Guid conversationId, IEnumerable<string> contexts)
    {
        try
        {
            if (contexts == null || !contexts.Any())
            {
                return 0;
            }

            // Guardar todos los contextos en batch (validación de duplicados incluida en el repositorio)
            var count = await _unitOfWork.ConversationContexts.CreateBatchAsync(conversationId, contexts);
            
            if (count > 0)
            {
                await _unitOfWork.SaveChangesAsync();
                _logger.LogDebug("Se agregaron {Count} contextos nuevos para conversación {ConversationId}", count, conversationId);
            }
            else
            {
                _logger.LogDebug("No se agregaron contextos nuevos para conversación {ConversationId} (todos eran duplicados o vacíos)", conversationId);
            }

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al agregar contextos en batch para conversación {ConversationId}", conversationId);
            throw;
        }
    }

}
