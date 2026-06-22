namespace MimosBabySpa.Application.Services;

public sealed record WhatsAppInboundDebounceMessage(
    Guid BusinessId,
    string Provider,
    string UserNumber,
    DateTime DueAtUtc);

public interface IWhatsAppInboundQueueService
{
    Task ScheduleDebounceAsync(
        Guid businessId,
        string provider,
        string userNumber,
        string providerMessageId,
        DateTime dueAtUtc,
        CancellationToken ct = default);
}
