using Auraly.Platform.Application.Campaigns.DTOs;

namespace Auraly.Platform.Application.Campaigns.Interfaces;

public interface ICampaignQueueService
{
    Task EnqueueAsync(CampaignDispatchMessage message, DateTime? scheduledAtUtc = null, CancellationToken ct = default);
}
