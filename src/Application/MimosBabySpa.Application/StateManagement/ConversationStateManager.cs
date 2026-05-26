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
        Verifications = DeserializeVerifications(entity.VerificationsJson),
        StageFactSnapshots = DeserializeStageSnapshots(entity.StageSnapshotsJson),
        CompletedOneShotStages = DeserializeStringSet(entity.CompletedStagesJson),
        CompletedActionStages = DeserializeStringSet(entity.CompletedActionStagesJson),
        LastAskedFact = entity.LastAskedFact,
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
        entity.VerificationsJson = state.Verifications.Count == 0
            ? null
            : JsonSerializer.Serialize(state.Verifications, JsonOptions);
        entity.StageSnapshotsJson = state.StageFactSnapshots.Count == 0
            ? null
            : JsonSerializer.Serialize(state.StageFactSnapshots, JsonOptions);
        entity.CompletedStagesJson = state.CompletedOneShotStages.Count == 0
            ? null
            : JsonSerializer.Serialize(state.CompletedOneShotStages, JsonOptions);
        entity.CompletedActionStagesJson = state.CompletedActionStages.Count == 0
            ? null
            : JsonSerializer.Serialize(state.CompletedActionStages, JsonOptions);
        entity.LastAskedFact = state.LastAskedFact;
        entity.Version = state.Version;
        entity.UpdatedAt = state.UpdatedAt;
    }

    private static Dictionary<string, VerificationEntry> DeserializeVerifications(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, VerificationEntry>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, VerificationEntry>>(json, JsonOptions)
                ?? new Dictionary<string, VerificationEntry>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, VerificationEntry>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, Dictionary<string, string>> DeserializeStageSnapshots(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, JsonOptions)
                ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }
    }

    private static HashSet<string> DeserializeStringSet(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new HashSet<string>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(json, JsonOptions)
                ?? new HashSet<string>(StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
