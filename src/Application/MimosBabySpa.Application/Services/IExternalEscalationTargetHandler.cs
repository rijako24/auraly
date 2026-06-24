using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IExternalEscalationTargetHandler
{
    bool CanHandle(string eventName, string targetType);

    Task OnAttemptSentAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default);

    Task OnAttemptCompletedAsync(ExternalEscalationAttempt attempt, ExternalEscalationCompletion completion, CancellationToken ct = default);

    Task OnAttemptDeclinedAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default);

    Task OnAttemptTimedOutAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default);

    Task OnAttemptsExhaustedAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default);
}

public sealed record ExternalEscalationCompletion(
    string OutcomeKey,
    string? ResponseText,
    IReadOnlyDictionary<string, string> Payload);
