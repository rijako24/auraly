using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IUsageLedgerRepository
{
    Task<UsageLedgerEntry> AddAsync(UsageLedgerEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<UsageLedgerEntry>> GetRecentByBusinessIdAsync(Guid businessId, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<UsageLedgerEntry>> GetByPeriodIdAsync(Guid businessUsagePeriodId, CancellationToken ct = default);
}
