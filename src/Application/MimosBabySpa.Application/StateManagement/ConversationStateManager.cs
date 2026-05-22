using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.StateManagement;

/// <summary>
/// Persiste el estado columnar del motor agentic (ConversationStateEntity).
/// </summary>
public class ConversationStateManager : IConversationStateManager
{
    private readonly ILogger<ConversationStateManager> _logger;
    private readonly IConversationStateRepository _stateRepository;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public ConversationStateManager(
        ILogger<ConversationStateManager> logger,
        IConversationStateRepository stateRepository)
    {
        _logger = logger;
        _stateRepository = stateRepository;
    }

    public async Task<ConversationState> GetOrCreateStateAsync(
        Guid conversationId,
        Guid businessId,
        string phone,
        CancellationToken cancellationToken = default)
    {
        var entity = await _stateRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        if (entity is not null)
        {
            _logger.LogDebug("Agent state loaded: ConversationId={ConversationId}, Version={Version}",
                conversationId, entity.Version);
            return MapToModel(entity);
        }

        var now = DateTime.UtcNow;
        var newEntity = new ConversationStateEntity
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            Owner = ConversationOwner.Bot,
            SessionStartedAt = now,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _stateRepository.SaveAsync(newEntity, cancellationToken);
        _logger.LogInformation("New agent state created for ConversationId={ConversationId}", conversationId);
        return MapToModel(newEntity);
    }

    public async Task<ConversationState?> GetStateByConversationIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        var entity = await _stateRepository.GetByConversationIdAsync(conversationId, ct);
        return entity is null ? null : MapToModel(entity);
    }

    public async Task<ConversationState> SaveStateAsync(
        Guid conversationId,
        ConversationState state,
        CancellationToken cancellationToken = default)
    {
        state.Version++;
        state.UpdatedAt = DateTime.UtcNow;

        var existing = await _stateRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        if (existing is not null && existing.Version > state.Version)
        {
            throw new InvalidOperationException(
                $"Conflict: DB version {existing.Version} > state version {state.Version}");
        }

        var entity = existing ?? new ConversationStateEntity
        {
            ConversationId = conversationId,
            BusinessId = state.BusinessId,
            CreatedAt = state.CreatedAt
        };

        MapToEntity(state, entity);
        await _stateRepository.SaveAsync(entity, cancellationToken);
        state.Version = entity.Version;

        _logger.LogDebug("Agent state saved: ConversationId={ConversationId}, Version={Version}",
            conversationId, state.Version);
        return state;
    }

    private static ConversationState MapToModel(ConversationStateEntity entity) => new()
    {
        ConversationId = entity.ConversationId,
        BusinessId = entity.BusinessId,
        Owner = entity.Owner,
        LastEscalatedAt = entity.LastEscalatedAt,
        ConsecutiveDegradedTurns = entity.ConsecutiveDegradedTurns,
        LastUserMessage = entity.LastUserMessage,
        LastBotMessage = entity.LastBotMessage,
        PreviousSession = DeserializePreviousSession(entity.PreviousSessionJson),
        SessionStartedAt = entity.SessionStartedAt,
        Version = entity.Version,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };

    private static void MapToEntity(ConversationState state, ConversationStateEntity entity)
    {
        entity.BusinessId = state.BusinessId;
        entity.Owner = state.Owner;
        entity.LastEscalatedAt = state.LastEscalatedAt;
        entity.ConsecutiveDegradedTurns = state.ConsecutiveDegradedTurns;
        entity.LastUserMessage = state.LastUserMessage;
        entity.LastBotMessage = state.LastBotMessage;
        entity.PreviousSessionJson = state.PreviousSession is null
            ? null
            : JsonSerializer.Serialize(state.PreviousSession, JsonOptions);
        entity.SessionStartedAt = state.SessionStartedAt;
        entity.Version = state.Version;
        entity.UpdatedAt = state.UpdatedAt;
    }

    private static PreviousSessionSnapshot? DeserializePreviousSession(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PreviousSessionSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
