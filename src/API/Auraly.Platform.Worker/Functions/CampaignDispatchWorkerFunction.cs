using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Campaigns.DTOs;
using Auraly.Platform.Application.Campaigns.Interfaces;
using Auraly.Platform.Infrastructure.Services;

namespace Auraly.Platform.Worker.Functions;

public sealed class CampaignDispatchWorkerFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICampaignDispatchService _dispatchService;
    private readonly ILogger<CampaignDispatchWorkerFunction> _logger;

    public CampaignDispatchWorkerFunction(
        ICampaignDispatchService dispatchService,
        ILogger<CampaignDispatchWorkerFunction> logger)
    {
        _dispatchService = dispatchService;
        _logger = logger;
    }

    [Function("CampaignDispatchWorker")]
    public async Task Run(
        [ServiceBusTrigger(CampaignQueueService.DefaultQueueName, Connection = "ServiceBusConnection", IsSessionsEnabled = true)] string body,
        CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<CampaignDispatchMessage>(body, JsonOptions)
            ?? throw new InvalidOperationException("Mensaje de campaña inválido.");

        _logger.LogInformation(
            "CampaignDispatchWorker: iniciando CampaignId={CampaignId}, BusinessId={BusinessId}",
            message.CampaignId,
            message.BusinessId);

        await _dispatchService.DispatchAsync(message.CampaignId, ct);
    }
}
