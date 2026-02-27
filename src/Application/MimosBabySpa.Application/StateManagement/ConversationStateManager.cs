using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Application.Services;
using System.Text.Json;

namespace MimosBabySpa.Application.StateManagement;

/// <summary>
/// Implementación del gestor de estado de conversación.
/// Persiste el estado en la base de datos usando ConversationStateEntity.
/// </summary>
public class ConversationStateManager : IConversationStateManager
{
    private readonly ILogger<ConversationStateManager> _logger;
    private readonly IConversationStateRepository _stateRepository;
    private readonly IConversationService _conversationService;

    public ConversationStateManager(
        ILogger<ConversationStateManager> logger,
        IConversationStateRepository stateRepository,
        IConversationService conversationService)
    {
        _logger = logger;
        _stateRepository = stateRepository;
        _conversationService = conversationService;
    }

    public async Task<ConversationState> GetOrCreateStateAsync(
        Guid conversationId,
        Guid businessId,
        string phone,
        CancellationToken cancellationToken = default)
    {
        // Cargar SIEMPRE desde la base de datos (sin cache)
        var stateEntity = await _stateRepository.GetByConversationIdAsync(conversationId, cancellationToken);

        if (stateEntity != null)
        {
            try
            {
                var state = DeserializeState(stateEntity.StateJson);
                state.StateId = Guid.NewGuid(); // Generar nuevo StateId para esta sesión
                state.Version = stateEntity.Version;
                state.UpdatedAt = stateEntity.UpdatedAt;
                state.CreatedAt = stateEntity.CreatedAt;

                _logger.LogInformation(
                    "Estado cargado desde BD: ConversationId={ConversationId}, Version={Version}, Attributes={AttributeCount}",
                    conversationId, state.Version, state.Attributes.Count);

                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializando estado desde BD. Creando nuevo estado.");
            }
        }

        // Crear nuevo estado
        var newState = new ConversationState
        {
            StateId = Guid.NewGuid(),
            BusinessId = businessId,
            Phone = phone,
            CurrentIntent = IntentType.Unknown,
            CurrentStage = TransactionStage.CollectingInformation,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Guardar en BD inmediatamente
        await SaveStateToDatabaseAsync(newState, conversationId, cancellationToken);

        _logger.LogInformation(
            "Nuevo estado creado y guardado en BD: StateId={StateId} para BusinessId={BusinessId}, Phone={Phone}, ConversationId={ConversationId}",
            newState.StateId, businessId, phone, conversationId);

        return newState;
    }

    public async Task<ConversationState?> GetStateByConversationIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        var stateEntity = await _stateRepository.GetByConversationIdAsync(conversationId, ct);
        if (stateEntity == null)
            return null;

        try
        {
            var state = DeserializeState(stateEntity.StateJson);
            state.Version = stateEntity.Version;
            state.UpdatedAt = stateEntity.UpdatedAt;
            state.CreatedAt = stateEntity.CreatedAt;
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializando estado por ConversationId={ConvId}", conversationId);
            return null;
        }
    }

    public async Task<ConversationState> SaveStateAsync(
        Guid conversationId,
        ConversationState state,
        CancellationToken cancellationToken = default)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        // Incrementar versión
        state.Version++;
        state.UpdatedAt = DateTime.UtcNow;

        // Guardar DIRECTAMENTE en base de datos (sin cache)
        await SaveStateToDatabaseAsync(state, conversationId, cancellationToken);

        _logger.LogInformation(
            "Estado guardado en BD: StateId={StateId}, Version={Version}, ConversationId={ConversationId}, Attributes={AttributeCount}",
            state.StateId, state.Version, conversationId, state.Attributes.Count);

        return state;
    }


    // ========================================
    // MÉTODOS PRIVADOS HELPER
    // ========================================

    private string GetKey(Guid businessId, string phone)
    {
        return $"{businessId}:{phone}";
    }

    /// <summary>
    /// Guarda el estado en la base de datos
    /// </summary>
    private async Task SaveStateToDatabaseAsync(
        ConversationState state,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var stateJson = SerializeState(state);

        // Verificar versión existente para optimistic locking
        var existingEntity = await _stateRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        
        if (existingEntity != null)
        {
            // Verificar versión para optimistic locking
            if (existingEntity.Version > state.Version)
            {
                _logger.LogWarning(
                    "Conflicto de versión en BD: ConversationId={ConversationId}, " +
                    "DBVersion={DBVersion}, StateVersion={StateVersion}",
                    conversationId, existingEntity.Version, state.Version);
                
                throw new InvalidOperationException(
                    $"Conflict: El estado en BD tiene versión {existingEntity.Version}, " +
                    $"pero el estado proporcionado tiene versión {state.Version}");
            }
        }

        // Crear o actualizar entidad
        var entity = existingEntity ?? new ConversationStateEntity
        {
            ConversationId = conversationId,
            BusinessId = state.BusinessId,
            CreatedAt = state.CreatedAt
        };

        entity.StateJson = stateJson;
        entity.Version = state.Version;
        entity.UpdatedAt = state.UpdatedAt;

        await _stateRepository.SaveAsync(entity, cancellationToken);

        _logger.LogDebug(
            "Estado guardado en BD: ConversationId={ConversationId}, Version={Version}",
            conversationId, state.Version);
    }

    /// <summary>
    /// Serializa el estado a JSON
    /// </summary>
    private string SerializeState(ConversationState state)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(state, options);
    }

    /// <summary>
    /// Deserializa el estado desde JSON
    /// </summary>
    private ConversationState DeserializeState(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var state = JsonSerializer.Deserialize<ConversationState>(json, options);
        
        if (state == null)
        {
            throw new InvalidOperationException("No se pudo deserializar el estado desde JSON");
        }

        // Asegurar que Attributes no sea null
        if (state.Attributes == null)
        {
            state.Attributes = new Dictionary<string, string>();
        }

        return state;
    }

    // Método de auditoría removido - sin cache en memoria, no hay historial temporal
    // La auditoría debe implementarse en BD si es necesaria

    // Métodos helper removidos - ya no se necesitan sin cache en memoria
}
