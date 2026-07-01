namespace MimosBabySpa.Application.Services;

public interface IBusinessInboundContactRouter
{
    Task<BusinessInboundContactRoute?> ResolveAsync(Guid businessId, string phone, CancellationToken ct = default);
}

public interface IExternalEscalationService
{
    Task<ExternalEscalationSendResult> EscalateAsync(ExternalEscalationRequest request, CancellationToken ct = default);

    Task<ExternalEscalationSendResult> EscalateEventAsync(
        Guid sourceAgentId,
        string eventName,
        Guid targetId,
        IReadOnlyDictionary<string, string> custom,
        CancellationToken ct = default);

    Task<ExternalEscalationSendResult> EscalateToolAsync(
        Guid sourceAgentId,
        string toolName,
        Guid targetId,
        IReadOnlyDictionary<string, string> custom,
        CancellationToken ct = default);

    Task<ExternalEscalationCompletionResult> CompleteAttemptAsync(
        ExternalEscalationCompletionRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExternalEscalationExpiredAttempt>> ProcessExpiredAttemptsAsync(CancellationToken ct = default);
}

