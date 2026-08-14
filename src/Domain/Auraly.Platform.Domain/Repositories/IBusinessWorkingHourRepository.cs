using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IBusinessWorkingHourRepository
{
    Task<IReadOnlyList<BusinessWorkingHour>> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default);
    Task ReplaceForBusinessAsync(Guid businessId, IEnumerable<BusinessWorkingHour> workingHours, CancellationToken ct = default);
}
