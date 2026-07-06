using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface ICampaignRepository
{
    Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken ct = default);
    Task<(IReadOnlyList<Campaign> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<Campaign> AddAsync(Campaign campaign, CancellationToken ct = default);
    Task AddRecipientsAsync(IEnumerable<CampaignRecipient> recipients, CancellationToken ct = default);
    Task<IReadOnlyList<CampaignRecipient>> GetDispatchableRecipientsAsync(
        Guid campaignId,
        int take,
        DateTime staleSendingBeforeUtc,
        CancellationToken ct = default);
    Task UpdateAsync(Campaign campaign, CancellationToken ct = default);
}
