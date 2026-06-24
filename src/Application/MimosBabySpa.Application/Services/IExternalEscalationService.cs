namespace MimosBabySpa.Application.Services;

public interface IBusinessInboundContactRouter
{
    Task<BusinessInboundContactRoute?> ResolveAsync(Guid businessId, string phone, CancellationToken ct = default);
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

    Task<ExternalEscalationActionResult> CompleteAsync(
        Guid businessId,
        Guid attemptId,
        string contactPhone,
        string outcomeKey,
        string? responseText,
        IReadOnlyDictionary<string, string>? responsePayload = null,
        CancellationToken ct = default);

    Task ProcessExpiredAttemptsAsync(CancellationToken ct = default);
}
