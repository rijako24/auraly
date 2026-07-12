using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Services;

public interface IRequestContextService
{
    Task<RequestContextCleanupResult> CompleteAsync(
        Guid conversationId,
        AgentConfig config,
        ConversationState state,
        IDictionary<string, string>? inMemoryFacts,
        string reason,
        CancellationToken ct = default);

    Task<RequestContextRolloverResult> ApplyRetentionAsync(
        Conversation conversation,
        AgentConfig config,
        ConversationState state,
        IDictionary<string, string> inMemoryFacts,
        BusinessClockSnapshot clock,
        CancellationToken ct = default);
}

public sealed record RequestContextCleanupResult(
    string Reason,
    IReadOnlyList<string> ClearedFacts,
    IReadOnlyList<string> PreservedFacts);

public sealed record RequestContextRolloverResult(
    bool BusinessDayChanged,
    DateOnly? PreviousBusinessDay,
    IReadOnlyList<string> ClearedFacts)
{
    public static RequestContextRolloverResult None { get; } = new(false, null, []);
}

public sealed class RequestContextService : IRequestContextService
{
    private readonly IConversationFactsService _facts;
    private readonly ILogger<RequestContextService> _logger;

    public RequestContextService(
        IConversationFactsService facts,
        ILogger<RequestContextService> logger)
    {
        _facts = facts;
        _logger = logger;
    }

    public async Task<RequestContextCleanupResult> CompleteAsync(
        Guid conversationId,
        AgentConfig config,
        ConversationState state,
        IDictionary<string, string>? inMemoryFacts,
        string reason,
        CancellationToken ct = default)
    {
        var customerKeys = config.FactSchema
            .Where(f => f.ShouldRememberAcrossRequests())
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var records = await _facts.GetAllRecordsAsync(conversationId, ct);
        var fieldsToClear = records
            .Select(r => r.Key)
            .Where(key => !customerKeys.Contains(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cleared = await _facts.ClearFieldsAsync(conversationId, fieldsToClear, ct);
        RemoveFromMemory(inMemoryFacts, cleared);
        state.ActiveRequestStartedAtUtc = DateTime.UtcNow;
        state.RequestGeneration++;
        ClearVolatileState(state);

        _logger.LogInformation(
            "Conv {ConvId}: request context completed reason={Reason}, cleared={Count}",
            conversationId, reason, cleared.Count);

        return new RequestContextCleanupResult(
            reason,
            cleared,
            customerKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task<RequestContextRolloverResult> ApplyRetentionAsync(
        Conversation conversation,
        AgentConfig config,
        ConversationState state,
        IDictionary<string, string> inMemoryFacts,
        BusinessClockSnapshot clock,
        CancellationToken ct = default)
    {
        var previousBusinessDay = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(conversation.LastActivityAt, clock.TimeZone));
        var businessDayChanged = clock.Today > previousBusinessDay;

        var records = await _facts.GetAllRecordsAsync(conversation.ConversationId, ct);
        if (records.Count == 0)
        {
            if (businessDayChanged)
                ClearVolatileState(state);

            return businessDayChanged
                ? new RequestContextRolloverResult(true, previousBusinessDay, [])
                : RequestContextRolloverResult.None;
        }

        var schemaByKey = config.FactSchema
            .Where(f => !string.IsNullOrWhiteSpace(f.Key))
            .GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var fieldsToClear = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (!schemaByKey.TryGetValue(record.Key, out var entry))
            {
                if (businessDayChanged && record.Key.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
                    fieldsToClear.Add(record.Key);

                continue;
            }

            var retention = entry.Retention();
            if (retention.HasValue
                && record.LastTouchedAt.Add(retention.Value) < clock.Now.UtcDateTime)
            {
                fieldsToClear.Add(record.Key);
                continue;
            }

            if (businessDayChanged && entry.ExpireOnBusinessDayChange)
                fieldsToClear.Add(record.Key);
        }

        ClearPastBookingDateFacts(config, inMemoryFacts, clock.Today, fieldsToClear);
        AddDependentFactsToClear(config, fieldsToClear);

        var cleared = await _facts.ClearFieldsAsync(conversation.ConversationId, fieldsToClear.ToList(), ct);
        RemoveFromMemory(inMemoryFacts, cleared);

        if (ClearedRequestScopedFact(config, cleared))
            state.ActiveRequestStartedAtUtc = clock.Now.UtcDateTime;

        if (businessDayChanged || cleared.Count > 0)
            ClearVolatileState(state);

        if (businessDayChanged || cleared.Count > 0)
        {
            _logger.LogInformation(
                "Conv {ConvId}: request context retention applied dayChanged={DayChanged}, cleared={Count}",
                conversation.ConversationId, businessDayChanged, cleared.Count);
        }

        return businessDayChanged
            ? new RequestContextRolloverResult(true, previousBusinessDay, cleared)
            : new RequestContextRolloverResult(false, null, cleared);
    }

    private static void ClearPastBookingDateFacts(
        AgentConfig config,
        IDictionary<string, string> facts,
        DateOnly businessToday,
        ISet<string> fieldsToClear)
    {
        var dateEntry = FindByRole(config, "booking.date");
        if (dateEntry is null
            || !facts.TryGetValue(dateEntry.Key, out var rawDate)
            || !DateOnly.TryParse(rawDate, out var desiredDate)
            || desiredDate >= businessToday)
        {
            return;
        }

        fieldsToClear.Add(dateEntry.Key);

        var timeEntry = FindByRole(config, "booking.time");
        if (timeEntry is not null)
            fieldsToClear.Add(timeEntry.Key);
    }

    private static void AddDependentFactsToClear(
        AgentConfig config,
        ISet<string> fieldsToClear)
    {
        if (fieldsToClear.Count == 0)
            return;

        var queue = new Queue<string>(fieldsToClear);
        while (queue.Count > 0)
        {
            var changedKey = queue.Dequeue();
            foreach (var entry in config.FactSchema)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)
                    || entry.IsCustomerScoped()
                    || fieldsToClear.Contains(entry.Key)
                    || entry.DependsOn.Count == 0
                    || !entry.DependsOn.Any(dependency =>
                        dependency.Equals(changedKey, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                fieldsToClear.Add(entry.Key);
                queue.Enqueue(entry.Key);
            }
        }
    }

    private static bool ClearedRequestScopedFact(AgentConfig config, IReadOnlyCollection<string> cleared)
    {
        if (cleared.Count == 0)
            return false;

        var clearedSet = cleared.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return config.FactSchema.Any(entry =>
            !string.IsNullOrWhiteSpace(entry.Key)
            && clearedSet.Contains(entry.Key)
            && entry.EffectiveScope().Equals(FactScopes.Request, StringComparison.OrdinalIgnoreCase));
    }

    private static FactSchemaEntry? FindByRole(AgentConfig config, string role) =>
        config.FactSchema.FirstOrDefault(f =>
            string.Equals(f.Role, role, StringComparison.OrdinalIgnoreCase));

    private static void RemoveFromMemory(IDictionary<string, string>? facts, IReadOnlyCollection<string> cleared)
    {
        if (facts is null)
            return;

        foreach (var key in cleared)
            facts.Remove(key);
    }

    private static void ClearVolatileState(ConversationState state)
    {
        state.Verifications.Clear();
        state.StageFactSnapshots.Clear();
    }
}
