using Auraly.Platform.Application.Configuration;

namespace Auraly.Platform.Application.Services;

public interface IWorkingHoursService
{
    Task<IReadOnlyList<TimeBlock>> GetEffectiveWorkingHoursAsync(
        Guid businessId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct = default);

    Task<IReadOnlyList<TimeBlock>> GetEffectiveBusinessWorkingHoursAsync(
        Guid businessId,
        DateOnly date,
        CancellationToken ct = default);
}
