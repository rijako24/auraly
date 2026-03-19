using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Models.Flow;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.GenericFlow.Services;

/// <summary>
/// Loads and persists FlowExecutionState from the database.
/// Handles session resets (abandonment timeout, completion flags).
/// </summary>
public class FlowStateManager
{
    private readonly IFlowExecutionStateRepository _repo;
    private readonly IFlowDefinitionRepository _flowDefRepo;
    private readonly ILogger<FlowStateManager> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FlowStateManager(
        IFlowExecutionStateRepository repo,
        IFlowDefinitionRepository flowDefRepo,
        ILogger<FlowStateManager> logger)
    {
        _repo = repo;
        _flowDefRepo = flowDefRepo;
        _logger = logger;
    }

    public async Task<(FlowExecutionState state, Guid flowDefinitionId)> LoadOrCreateAsync(
        string userIdentifier,
        Agent agent,
        FlowDefinitionDocument flow,
        CancellationToken ct)
    {
        var flowDef = await _flowDefRepo.GetActiveByAgentAsync(agent.AgentId, ct)
            ?? throw new InvalidOperationException($"No active flow definition found for agent {agent.AgentId}");

        var businessId = agent.BusinessId;
        var entity = await _repo.GetAsync(businessId, userIdentifier, agent.AgentId, ct);

        if (entity == null)
        {
            var fresh = new FlowExecutionState { SessionStartedAt = DateTime.UtcNow };
            _logger.LogInformation("New session for {Identifier} agent={AgentId}", userIdentifier, agent.AgentId);
            return (fresh, flowDef.FlowDefinitionId);
        }

        var state = Deserialize(entity);

        // Check abandonment timeout
        var timeout = TimeSpan.FromHours(flow.SessionConfig.AbandonmentTimeoutHours);
        if (DateTime.UtcNow - entity.UpdatedAt > timeout)
        {
            _logger.LogInformation("Session timeout for {Identifier} — resetting", userIdentifier);
            state = ResetSession(state, flow);
        }

        // Check completion flags
        if (flow.SessionConfig.ResetOnFlowCompletion &&
            flow.SessionConfig.CompletionFlags.Any(f => state.GetFlag(f)))
        {
            _logger.LogInformation("Flow completed for {Identifier} — resetting for next session", userIdentifier);
            state = ResetSession(state, flow);
        }

        return (state, flowDef.FlowDefinitionId);
    }

    public async Task SaveAsync(
        string userIdentifier,
        Agent agent,
        Guid flowDefinitionId,
        FlowExecutionState state,
        CancellationToken ct)
    {
        state.UpdatedAt = DateTime.UtcNow;
        state.Version++;

        var entity = new FlowExecutionStateEntity
        {
            BusinessId = agent.BusinessId,
            UserIdentifier = userIdentifier,
            AgentId = agent.AgentId,
            FlowDefinitionId = flowDefinitionId,
            CurrentNodeId = state.CurrentNodeId,
            IsWaitingForUser = state.IsWaitingForUser,
            Owner = state.Owner.ToString(),
            Version = state.Version,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt,
            SessionStartedAt = state.SessionStartedAt,
            VariablesJson = JsonSerializer.Serialize(state.Variables, JsonOpts),
            FlagsJson = JsonSerializer.Serialize(state.Flags, JsonOpts),
            ActionResultsJson = JsonSerializer.Serialize(state.ActionResults, JsonOpts),
            TraceJson = state.Trace.Count > 0
                ? JsonSerializer.Serialize(state.Trace, JsonOpts)
                : null,
            PreviousSessionJson = state.PreviousSession != null
                ? JsonSerializer.Serialize(state.PreviousSession, JsonOpts)
                : null,
            ConversationHistoryJson = state.ConversationHistory.Count > 0
                ? JsonSerializer.Serialize(state.ConversationHistory, JsonOpts)
                : null,
            ConsecutiveDegradedTurns = state.ConsecutiveDegradedTurns
        };

        await _repo.UpsertAsync(entity, ct);
    }

    private static FlowExecutionState Deserialize(FlowExecutionStateEntity entity)
    {
        return new FlowExecutionState
        {
            CurrentNodeId = entity.CurrentNodeId,
            IsWaitingForUser = entity.IsWaitingForUser,
            Owner = Enum.TryParse<ConversationOwner>(entity.Owner, out var o) ? o : ConversationOwner.Bot,
            Version = entity.Version,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            SessionStartedAt = entity.SessionStartedAt,
            Variables = DeserializeDict<string?>(entity.VariablesJson),
            Flags = DeserializeDict<bool>(entity.FlagsJson),
            ActionResults = DeserializeActionResults(entity.ActionResultsJson),
            Trace = DeserializeList<FlowTraceEntry>(entity.TraceJson),
            PreviousSession = entity.PreviousSessionJson != null
                ? JsonSerializer.Deserialize<PreviousFlowSessionSnapshot>(entity.PreviousSessionJson, JsonOpts)
                : null,
            ConversationHistory = DeserializeList<FlowConversationMessage>(entity.ConversationHistoryJson),
            ConsecutiveDegradedTurns = entity.ConsecutiveDegradedTurns
        };
    }

    private static FlowExecutionState ResetSession(FlowExecutionState old, FlowDefinitionDocument flow)
    {
        var snapshot = new PreviousFlowSessionSnapshot
        {
            SessionEndedAt = DateTime.UtcNow,
            PersistentVariables = flow.SessionConfig.PersistentVariables
                .ToDictionary(k => k, k => old.GetVariable(k))
        };

        var fresh = new FlowExecutionState
        {
            SessionStartedAt = DateTime.UtcNow,
            PreviousSession = snapshot
        };

        // Preserve persistent variables
        foreach (var key in flow.SessionConfig.PersistentVariables)
        {
            var val = old.GetVariable(key);
            if (val != null) fresh.Variables[key] = val;
        }

        return fresh;
    }

    private static Dictionary<string, T> DeserializeDict<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, T>>(json, JsonOpts)
                   ?? new Dictionary<string, T>();
        }
        catch { return new Dictionary<string, T>(); }
    }

    private static List<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    private static Dictionary<string, Dictionary<string, object?>> DeserializeActionResults(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object?>>>(json, JsonOpts)
                   ?? new();
        }
        catch { return new(); }
    }
}
