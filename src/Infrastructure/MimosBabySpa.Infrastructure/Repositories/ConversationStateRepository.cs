using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

/// <summary>
/// Implementación del repositorio de estado de conversación usando EF Core y serialización JSON.
/// </summary>
public class ConversationStateRepository : IConversationStateRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ConversationStateRepository> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ConversationStateRepository(
        ApplicationDbContext context,
        ILogger<ConversationStateRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ConversationState> GetAsync(Guid conversationId)
    {
        try
        {
            var entity = await _context.ConversationStates
                .FirstOrDefaultAsync(cs => cs.ConversationId == conversationId);

            if (entity == null)
            {
                _logger.LogDebug("No se encontró estado para conversación {ConversationId}, retornando estado vacío", conversationId);
                return new ConversationState();
            }

            var state = JsonSerializer.Deserialize<ConversationState>(entity.StateJson, JsonOptions);
            if (state == null)
            {
                _logger.LogWarning("Error al deserializar estado para conversación {ConversationId}, retornando estado vacío", conversationId);
                return new ConversationState();
            }

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener estado de conversación {ConversationId}", conversationId);
            return new ConversationState();
        }
    }

    public async Task SaveAsync(Guid conversationId, Guid businessId, ConversationState state)
    {
        try
        {
            // Actualizar versión y timestamp
            state.Version++;
            state.UpdatedAt = DateTime.UtcNow;

            var stateJson = JsonSerializer.Serialize(state, JsonOptions);

            var entity = await _context.ConversationStates
                .FirstOrDefaultAsync(cs => cs.ConversationId == conversationId);

            if (entity == null)
            {
                // Crear nuevo estado
                entity = new ConversationStateEntity
                {
                    ConversationId = conversationId,
                    BusinessId = businessId,
                    StateJson = stateJson,
                    Version = state.Version,
                    UpdatedAt = state.UpdatedAt,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ConversationStates.Add(entity);
            }
            else
            {
                // Actualizar estado existente
                entity.StateJson = stateJson;
                entity.Version = state.Version;
                entity.UpdatedAt = state.UpdatedAt;
                _context.ConversationStates.Update(entity);
            }

            await _context.SaveChangesAsync();

            _logger.LogDebug(
                "Estado guardado para conversación {ConversationId}, versión {Version}",
                conversationId, state.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar estado de conversación {ConversationId}", conversationId);
            throw;
        }
    }

    public async Task DeleteAsync(Guid conversationId)
    {
        try
        {
            var entity = await _context.ConversationStates
                .FirstOrDefaultAsync(cs => cs.ConversationId == conversationId);

            if (entity != null)
            {
                _context.ConversationStates.Remove(entity);
                await _context.SaveChangesAsync();
                _logger.LogDebug("Estado eliminado para conversación {ConversationId}", conversationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar estado de conversación {ConversationId}", conversationId);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(Guid conversationId)
    {
        return await _context.ConversationStates
            .AnyAsync(cs => cs.ConversationId == conversationId);
    }
}
