using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IBusinessWorkingHourRepository
{
    Task<IReadOnlyList<BusinessWorkingHour>> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default);
    Task ReplaceForBusinessAsync(Guid businessId, IEnumerable<BusinessWorkingHour> workingHours, CancellationToken ct = default);
}
