using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using MimosBabySpa.Application.Campaigns.DTOs;
using MimosBabySpa.Application.Campaigns.Interfaces;

namespace MimosBabySpa.Infrastructure.Services;

public sealed class CampaignQueueService : ICampaignQueueService, IAsyncDisposable
{
    public const string DefaultQueueName = "campaign-dispatch";

    private readonly IConfiguration _configuration;
    private ServiceBusClient? _client;
    private ServiceBusSender? _sender;

    public CampaignQueueService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task EnqueueAsync(
        CampaignDispatchMessage message,
        DateTime? scheduledAtUtc = null,
        CancellationToken ct = default)
    {
        var serviceBusMessage = new ServiceBusMessage(JsonSerializer.Serialize(message))
        {
            MessageId = $"campaign:{message.CampaignId:N}",
            SessionId = message.BusinessId.ToString("N"),
            ContentType = "application/json"
        };

        if (scheduledAtUtc.HasValue)
            serviceBusMessage.ScheduledEnqueueTime = new DateTimeOffset(DateTime.SpecifyKind(scheduledAtUtc.Value, DateTimeKind.Utc));

        await GetSender().SendMessageAsync(serviceBusMessage, ct);
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

        var connectionString = _configuration["ServiceBusConnection"]
            ?? _configuration.GetConnectionString("ServiceBus")
            ?? throw new InvalidOperationException("ServiceBusConnection debe estar configurado.");

        _client = new ServiceBusClient(connectionString);
        _sender = _client.CreateSender(DefaultQueueName);
        return _sender;
    }
}
