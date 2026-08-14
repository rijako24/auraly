using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Extensions;

namespace Auraly.Platform.Infrastructure.Repositories;

public class CampaignRepository : ICampaignRepository
{
    private readonly ApplicationDbContext _context;

    public CampaignRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken ct = default)
    {
        return await _context.Campaigns
            .Include(c => c.Recipients.OrderBy(r => r.CreatedAt).Take(200))
            .FirstOrDefaultAsync(c => c.CampaignId == campaignId, ct);
    }

    public async Task<(IReadOnlyList<Campaign> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        var query = _context.Campaigns
            .Where(c => c.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(s) ||
                c.TemplateName.ToLower().Contains(s));
        }

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .ToPagedListAsync(page, pageSize, ct);
    }

    public async Task<Campaign> AddAsync(Campaign campaign, CancellationToken ct = default)
    {
        await _context.Campaigns.AddAsync(campaign, ct);
        return campaign;
    }

    public async Task AddRecipientsAsync(IEnumerable<CampaignRecipient> recipients, CancellationToken ct = default)
    {
        await _context.CampaignRecipients.AddRangeAsync(recipients, ct);
    }

    public async Task<IReadOnlyList<CampaignRecipient>> GetDispatchableRecipientsAsync(
        Guid campaignId,
        int take,
        DateTime staleSendingBeforeUtc,
        CancellationToken ct = default)
    {
        return await _context.CampaignRecipients
            .Where(r => r.CampaignId == campaignId
                && (r.Status == "Pending"
                    || (r.Status == "Sending"
                        && r.LastAttemptAtUtc != null
                        && r.LastAttemptAtUtc <= staleSendingBeforeUtc)))
            .OrderBy(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task UpdateAsync(Campaign campaign, CancellationToken ct = default)
    {
        _context.Campaigns.Update(campaign);
        return Task.CompletedTask;
    }
}
