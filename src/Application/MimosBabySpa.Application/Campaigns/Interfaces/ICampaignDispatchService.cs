namespace MimosBabySpa.Application.Campaigns.Interfaces;

public interface ICampaignDispatchService
{
    Task DispatchAsync(Guid campaignId, CancellationToken ct = default);
}
