using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IBusinessUsagePeriodRepository
{
    Task<BusinessUsagePeriod?> GetCurrentAsync(Guid businessSubscriptionId, DateTime utcNow, CancellationToken ct = default);
    Task<BusinessUsagePeriod?> GetCurrentByBusinessIdAsync(Guid businessId, DateTime utcNow, CancellationToken ct = default);
    Task<BusinessUsagePeriod> AddAsync(BusinessUsagePeriod period, CancellationToken ct = default);
    Task UpdateAsync(BusinessUsagePeriod period, CancellationToken ct = default);
}
