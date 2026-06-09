using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IUsageCostRateRepository
{
    Task<UsageCostRate?> GetActiveAsync(string code, UsageOperationType operationType, DateTime utcNow, CancellationToken ct = default);
    Task<IReadOnlyList<UsageCostRate>> GetActiveAsync(CancellationToken ct = default);
}
