using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface ISubscriptionPlanRepository
{
    Task<IReadOnlyList<SubscriptionPlan>> GetActiveAsync(CancellationToken ct = default);
    Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default);
}
