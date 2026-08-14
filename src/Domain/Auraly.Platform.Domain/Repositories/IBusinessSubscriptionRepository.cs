using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IBusinessSubscriptionRepository
{
    Task<BusinessSubscription?> GetActiveByBusinessIdAsync(Guid businessId, CancellationToken ct = default);
    Task<BusinessSubscription> AddAsync(BusinessSubscription subscription, CancellationToken ct = default);
    Task UpdateAsync(BusinessSubscription subscription, CancellationToken ct = default);
}
