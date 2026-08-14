using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Repositories;

public interface IUsageCostRateRepository
{
    Task<UsageCostRate?> GetActiveAsync(string code, UsageOperationType operationType, DateTime utcNow, CancellationToken ct = default);
    Task<IReadOnlyList<UsageCostRate>> GetActiveAsync(CancellationToken ct = default);
}
