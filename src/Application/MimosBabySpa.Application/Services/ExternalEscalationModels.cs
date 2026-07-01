using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Services;

public sealed record BusinessInboundContactRoute(Guid AgentId, string ContactKey, string ContactPhone);

public static class ExternalEscalationOutcomeKeys
{
    public const string Requested = "requested";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string TimedOut = "timed_out";
}

public sealed record ExternalEscalationRequest(
    Guid SourceAgentId,
    string EventName,
    Guid TargetId,
    IReadOnlyDictionary<string, string> Custom);

public sealed record ExternalEscalationSendResult(bool Sent, string? Code, string? Error, Guid? InteractionId = null);

public sealed record ExternalEscalationCompletionRequest(
    Guid BusinessId,
    Guid AttemptId,
    string ContactPhone,
    string OutcomeKey,
    ExternalEscalationAttemptStatus CompletedStatus,
    string? ResponseText,
    IReadOnlyDictionary<string, string>? Payload = null);

public sealed record ExternalEscalationCompletionResult(
    bool Success,
    ExternalEscalationAttempt? Attempt,
    string Message,
    string? OutcomeKey = null,
    IReadOnlyDictionary<string, string>? Payload = null);

public sealed record ExternalEscalationExpiredAttempt(
    Guid BusinessId,
    Guid AttemptId,
    string EventName,
    string TargetType,
    Guid TargetId,
    string OutcomeKey,
    IReadOnlyDictionary<string, string> Payload);

