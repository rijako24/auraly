using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Services;

public interface IWorkingHoursService
{
    Task<IReadOnlyList<TimeBlock>> GetEffectiveWorkingHoursAsync(
        Guid businessId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct = default);
}
