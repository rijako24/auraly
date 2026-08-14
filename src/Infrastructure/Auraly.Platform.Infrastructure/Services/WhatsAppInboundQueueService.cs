using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Auraly.Platform.Infrastructure.Configuration;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Infrastructure.Services;

public sealed class WhatsAppInboundQueueService : IWhatsAppInboundQueueService, IAsyncDisposable
{
    public const string DefaultQueueName = "whatsapp-inbound-debounce";

    private readonly IConfiguration _configuration;
    private ServiceBusClient? _client;
    private ServiceBusSender? _sender;

    public WhatsAppInboundQueueService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task ScheduleDebounceAsync(
        Guid businessId,
        string provider,
        string userNumber,
        string providerMessageId,
        DateTime dueAtUtc,
        CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new WhatsAppInboundDebounceMessage(
            businessId,
            provider,
            userNumber,
            dueAtUtc));

        var message = new ServiceBusMessage(body)
        {
            MessageId = $"wa-debounce:{businessId:N}:{providerMessageId}",
            SessionId = $"{businessId:N}:{userNumber}",
            ScheduledEnqueueTime = new DateTimeOffset(DateTime.SpecifyKind(dueAtUtc, DateTimeKind.Utc)),
            ContentType = "application/json"
        };

        await GetSender().SendMessageAsync(message, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender is not null)
            await _sender.DisposeAsync();
        if (_client is not null)
            await _client.DisposeAsync();
    }

    private ServiceBusSender GetSender()
    {
        if (_sender is not null)
            return _sender;

        _client = AzureManagedClientFactory.CreateServiceBusClient(_configuration);
        _sender = _client.CreateSender(DefaultQueueName);
        return _sender;
    }
}
