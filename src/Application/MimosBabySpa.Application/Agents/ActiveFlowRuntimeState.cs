using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents;

internal sealed record ActiveFlowState(
    string FlowId,
    DateTime ActivatedAtUtc,
    DateTime LastTurnAtUtc,
    DateTime ExpiresAtUtc,
    string Decision,
    string Reason);

internal static class ActiveFlowRuntimeState
{
    private const string Key = "__active_flow";
    private const string FlowIdKey = "flow_id";
    private const string ActivatedAtUtcKey = "activated_at_utc";
    private const string LastTurnAtUtcKey = "last_turn_at_utc";
    private const string ExpiresAtUtcKey = "expires_at_utc";
    private const string DecisionKey = "decision";
    private const string ReasonKey = "reason";

    public static ActiveFlowState? Get(ConversationState? state)
    {
        if (state is null || !state.StageFactSnapshots.TryGetValue(Key, out var data))
            return null;

        if (!data.TryGetValue(FlowIdKey, out var flowId) || string.IsNullOrWhiteSpace(flowId))
            return null;

        return new ActiveFlowState(
            flowId.Trim(),
            ReadDate(data, ActivatedAtUtcKey),
            ReadDate(data, LastTurnAtUtcKey),
            ReadDate(data, ExpiresAtUtcKey),
            Read(data, DecisionKey),
            Read(data, ReasonKey));
    }

    public static void Set(
        ConversationState state,
        string flowId,
        DateTime nowUtc,
        TimeSpan ttl,
        string decision,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(flowId))
            return;

        var existing = Get(state);
        state.StageFactSnapshots[Key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FlowIdKey] = flowId.Trim(),
            [ActivatedAtUtcKey] = (existing?.ActivatedAtUtc ?? nowUtc).ToString("O"),
            [LastTurnAtUtcKey] = nowUtc.ToString("O"),
            [ExpiresAtUtcKey] = nowUtc.Add(ttl).ToString("O"),
            [DecisionKey] = decision,
            [ReasonKey] = reason
        };
    }

    public static void Clear(ConversationState? state)
    {
        state?.StageFactSnapshots.Remove(Key);
    }

    private static string Read(IReadOnlyDictionary<string, string> data, string key) =>
        data.TryGetValue(key, out var value) ? value : string.Empty;

    private static DateTime ReadDate(IReadOnlyDictionary<string, string> data, string key) =>
        DateTime.TryParse(Read(data, key), out var value) ? value.ToUniversalTime() : DateTime.MinValue;
}