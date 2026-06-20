namespace MimosBabySpa.Application.Services;

public interface IExternalEscalationRouter
{
    Task<ExternalEscalationRoute?> ResolveAsync(Guid businessId, string phone, CancellationToken ct = default);
}

public interface IExternalEscalationService
{
    Task<ExternalEscalationSendResult> EscalateNextAsync(ExternalEscalationRequest request, CancellationToken ct = default);

    Task<ExternalEscalationResolution> ResolveAttemptAsync(
        Guid businessId,
        string contactPhone,
        string messageText,
        string? interactivePayload,
        string? replyToProviderMessageId,
        CancellationToken ct = default);

    Task<ExternalEscalationActionResult> AcceptAsync(Guid businessId, Guid attemptId, string contactPhone, CancellationToken ct = default);

    Task<ExternalEscalationActionResult> DeclineAsync(Guid businessId, Guid attemptId, string contactPhone, CancellationToken ct = default);

    Task ProcessExpiredAttemptsAsync(CancellationToken ct = default);
}
