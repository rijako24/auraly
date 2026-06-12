namespace MimosBabySpa.Application.Services;

public interface IInboundMessageDeduplicationService
{
    Task<bool> TryBeginProcessingAsync(
        Guid businessId,
        string provider,
        string providerMessageId,
        CancellationToken ct = default);

    Task MarkProcessedAsync(
        Guid businessId,
        string provider,
        IEnumerable<string> providerMessageIds,
        CancellationToken ct = default);

    Task MarkFailedAsync(
        Guid businessId,
        string provider,
        IEnumerable<string> providerMessageIds,
        string error,
        CancellationToken ct = default);
}
