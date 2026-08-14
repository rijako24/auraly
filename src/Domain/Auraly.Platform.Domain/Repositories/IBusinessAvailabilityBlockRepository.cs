using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IBusinessAvailabilityBlockRepository
{
    Task<BusinessAvailabilityBlock?> GetByIdAsync(Guid businessAvailabilityBlockId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessAvailabilityBlock>> GetByBusinessAndDateAsync(Guid businessId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessAvailabilityBlock>> GetByBusinessAndDateRangeAsync(Guid businessId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default);
    Task<BusinessAvailabilityBlock> AddAsync(BusinessAvailabilityBlock block, CancellationToken ct = default);
    Task<BusinessAvailabilityBlock> UpdateAsync(BusinessAvailabilityBlock block, CancellationToken ct = default);
}
