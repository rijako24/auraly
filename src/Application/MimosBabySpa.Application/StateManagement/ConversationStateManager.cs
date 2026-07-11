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
            ActiveRequestStartedAtUtc = now,
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
        var expectedVersion = state.Version;
        var existing = await _stateRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        if (existing is not null && existing.Version != expectedVersion)
        {
            throw new InvalidOperationException(
                $"Conversation state conflict: expected version {expectedVersion}, database version {existing.Version}.");
        }

        state.Version = expectedVersion + 1;
        state.UpdatedAt = DateTime.UtcNow;
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

    private static ConversationState MapToModel(ConversationStateEntity entity)
    {
        var runtime = DeserializeRuntimeState(entity.RuntimeStateJson);
        return new ConversationState
        {
            ConversationId = entity.ConversationId,
            BusinessId = entity.BusinessId,
            Owner = entity.Owner,
            LastEscalatedAt = entity.LastEscalatedAt,
            ConsecutiveDegradedTurns = entity.ConsecutiveDegradedTurns,
            LastUserMessage = entity.LastUserMessage,
            LastBotMessage = entity.LastBotMessage,
            ActiveRequestStartedAtUtc = entity.ActiveRequestStartedAtUtc,
            Verifications = DeserializeVerifications(entity.VerificationsJson),
            StageFactSnapshots = DeserializeStageSnapshots(entity.StageSnapshotsJson),
            ActiveFlowId = runtime.ActiveFlowId,
            ActiveStageId = runtime.ActiveStageId,
            FactVersions = new Dictionary<string, long>(runtime.FactVersions, StringComparer.OrdinalIgnoreCase),
            PendingTurnPlan = runtime.PendingTurnPlan,
            RequestGeneration = runtime.RequestGeneration,
            ExecutedOperationKeys = new Dictionary<string, DateTime>(runtime.ExecutedOperationKeys, StringComparer.OrdinalIgnoreCase),
            Version = entity.Version,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static void MapToEntity(ConversationState state, ConversationStateEntity entity)
    {
        entity.BusinessId = state.BusinessId;
        entity.Owner = state.Owner;
        entity.LastEscalatedAt = state.LastEscalatedAt;
        entity.ConsecutiveDegradedTurns = state.ConsecutiveDegradedTurns;
        entity.LastUserMessage = state.LastUserMessage;
        entity.LastBotMessage = state.LastBotMessage;
        entity.ActiveRequestStartedAtUtc = state.ActiveRequestStartedAtUtc;
        entity.VerificationsJson = state.Verifications.Count == 0
            ? null
            : JsonSerializer.Serialize(state.Verifications, JsonOptions);
        entity.StageSnapshotsJson = state.StageFactSnapshots.Count == 0
            ? null
            : JsonSerializer.Serialize(state.StageFactSnapshots, JsonOptions);
        entity.RuntimeStateJson = JsonSerializer.Serialize(new DeterministicRuntimeState
        {
            SchemaVersion = 1,
            ActiveFlowId = state.ActiveFlowId,
            ActiveStageId = state.ActiveStageId,
            FactVersions = state.FactVersions,
            PendingTurnPlan = state.PendingTurnPlan,
            RequestGeneration = state.RequestGeneration,
            ExecutedOperationKeys = state.ExecutedOperationKeys
        }, JsonOptions);
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
    private static DeterministicRuntimeState DeserializeRuntimeState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new DeterministicRuntimeState();

        try
        {
            return JsonSerializer.Deserialize<DeterministicRuntimeState>(json, JsonOptions)
                ?? new DeterministicRuntimeState();
        }
        catch (JsonException)
        {
            return new DeterministicRuntimeState();
        }
    }

    private sealed class DeterministicRuntimeState
    {
        public int SchemaVersion { get; init; } = 1;
        public string? ActiveFlowId { get; init; }
        public string? ActiveStageId { get; init; }
        public Dictionary<string, long> FactVersions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public PendingTurnPlan? PendingTurnPlan { get; init; }
        public long RequestGeneration { get; init; }
        public Dictionary<string, DateTime> ExecutedOperationKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
