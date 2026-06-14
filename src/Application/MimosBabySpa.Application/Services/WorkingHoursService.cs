using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class WorkingHoursService : IWorkingHoursService
{
    private readonly IUnitOfWork _unitOfWork;

    public WorkingHoursService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<TimeBlock>> GetEffectiveWorkingHoursAsync(
        Guid businessId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var exceptions = await _unitOfWork.EmployeeScheduleExceptions
            .GetByEmployeeIdsAndDateAsync([employeeId], date, ct);

        if (exceptions.Count > 0)
        {
            if (exceptions.Any(e => e.IsClosed))
                return [];

            return exceptions
                .Where(e => e.OpenTime.HasValue && e.CloseTime.HasValue && e.OpenTime.Value < e.CloseTime.Value)
                .OrderBy(e => e.OpenTime)
                .Select(e => ToTimeBlock(e.OpenTime!.Value, e.CloseTime!.Value))
                .ToList();
        }

        var employeeHours = await _unitOfWork.EmployeeWorkingHours.GetByEmployeeIdAsync(employeeId, ct);
        if (employeeHours.Count > 0)
        {
            return employeeHours
                .Where(h => h.DayOfWeek == date.DayOfWeek && h.OpenTime < h.CloseTime)
                .OrderBy(h => h.OpenTime)
                .Select(h => ToTimeBlock(h.OpenTime, h.CloseTime))
                .ToList();
        }

        var businessHours = await _unitOfWork.BusinessWorkingHours.GetByBusinessIdAsync(businessId, ct);
        return businessHours
            .Where(h => h.DayOfWeek == date.DayOfWeek && h.OpenTime < h.CloseTime)
            .OrderBy(h => h.OpenTime)
            .Select(h => ToTimeBlock(h.OpenTime, h.CloseTime))
            .ToList();
    }

    private static TimeBlock ToTimeBlock(TimeSpan open, TimeSpan close) => new()
    {
        Open = open.ToString(@"hh\:mm"),
        Close = close.ToString(@"hh\:mm")
    };
}
