using MimosBabySpa.Application.Campaigns.DTOs;

namespace MimosBabySpa.Application.Campaigns.Interfaces;

public interface ICampaignQueueService
{
    Task EnqueueAsync(CampaignDispatchMessage message, DateTime? scheduledAtUtc = null, CancellationToken ct = default);
}
