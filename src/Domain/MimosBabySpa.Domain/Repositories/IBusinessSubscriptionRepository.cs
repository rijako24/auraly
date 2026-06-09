using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IBusinessSubscriptionRepository
{
    Task<BusinessSubscription?> GetActiveByBusinessIdAsync(Guid businessId, CancellationToken ct = default);
    Task<BusinessSubscription> AddAsync(BusinessSubscription subscription, CancellationToken ct = default);
    Task UpdateAsync(BusinessSubscription subscription, CancellationToken ct = default);
}
