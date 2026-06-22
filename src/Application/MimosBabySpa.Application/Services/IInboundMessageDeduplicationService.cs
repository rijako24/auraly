using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IInboundMessageDeduplicationService
{
    Task<bool> TryBeginProcessingAsync(
        Guid businessId,
        string provider,
        string providerMessageId,
        CancellationToken ct = default);

    Task<bool> TryRecordReceivedAsync(
        Guid businessId,
        string provider,
        string providerMessageId,
        string userNumber,
        string? customerName,
        string rawEntryJson,
        DateTime receivedAtUtc,
        DateTime processingDueAtUtc,
        CancellationToken ct = default);

    Task MarkQueuedAsync(
        Guid businessId,
        string provider,
        string providerMessageId,
        DateTime processingDueAtUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<InboundMessageReceipt>> GetPendingConversationMessagesAsync(
        Guid businessId,
        string provider,
        string userNumber,
        CancellationToken ct = default);

    Task MarkProcessingAsync(
        Guid businessId,
        string provider,
        IEnumerable<string> providerMessageIds,
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
