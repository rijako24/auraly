using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IUsageLedgerRepository
{
    Task<UsageLedgerEntry> AddAsync(UsageLedgerEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<UsageLedgerEntry>> GetRecentByBusinessIdAsync(Guid businessId, int limit, CancellationToken ct = default);
}
