using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface ISubscriptionPlanRepository
{
    Task<IReadOnlyList<SubscriptionPlan>> GetActiveAsync(CancellationToken ct = default);
    Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default);
}
