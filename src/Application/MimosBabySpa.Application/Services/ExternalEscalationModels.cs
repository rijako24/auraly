using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public sealed record ExternalEscalationRoute(Guid AgentId, string ContactKey, string ContactPhone);

public sealed record ExternalEscalationRequest(
    Guid SourceAgentId,
    string EventName,
    string TargetType,
    Guid TargetId,
    IReadOnlyDictionary<string, string> Custom);

public sealed record ExternalEscalationSendResult(bool Sent, string? Code, string? Error);

public sealed record ExternalEscalationResolution(
    string Resolution,
    ExternalEscalationAttempt? Attempt,
    IReadOnlyList<ExternalEscalationAttempt> PendingAttempts,
    string? Error,
    string? RequestedAction = null);

public sealed record ExternalEscalationActionResult(bool Success, ExternalEscalationAttempt? Attempt, string Message, bool EscalatedNext);

internal sealed record ExternalEscalationContactMatch(
    Guid SourceAgentId,
    Guid BusinessId,
    string EventName,
    ExternalEscalationContactDefinition Contact);
