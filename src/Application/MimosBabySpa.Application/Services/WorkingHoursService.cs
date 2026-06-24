using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
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
        var baseBlocks = await GetBaseWorkingHoursAsync(businessId, employeeId, date, ct);
        if (baseBlocks.Count == 0)
            return [];

        var availabilityBlocks = await _unitOfWork.BusinessAvailabilityBlocks.GetByBusinessAndDateAsync(businessId, date, ct);
        var applicableBlocks = availabilityBlocks
            .Where(b => b.IsActive && (!b.EmployeeId.HasValue || b.EmployeeId.Value == employeeId))
            .ToList();

        return ApplyAvailabilityBlocks(baseBlocks, applicableBlocks);
    }

    public async Task<IReadOnlyList<TimeBlock>> GetEffectiveBusinessWorkingHoursAsync(
        Guid businessId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var businessHours = await _unitOfWork.BusinessWorkingHours.GetByBusinessIdAsync(businessId, ct);
        var baseBlocks = businessHours
            .Where(h => h.DayOfWeek == date.DayOfWeek && h.OpenTime < h.CloseTime)
            .OrderBy(h => h.OpenTime)
            .Select(h => ToTimeBlock(h.OpenTime, h.CloseTime))
            .ToList();

        if (baseBlocks.Count == 0)
            return [];

        var availabilityBlocks = await _unitOfWork.BusinessAvailabilityBlocks.GetByBusinessAndDateAsync(businessId, date, ct);
        var businessBlocks = availabilityBlocks
            .Where(b => b.IsActive && !b.EmployeeId.HasValue)
            .ToList();

        return ApplyAvailabilityBlocks(baseBlocks, businessBlocks);
    }
    private async Task<IReadOnlyList<TimeBlock>> GetBaseWorkingHoursAsync(
        Guid businessId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct)
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

    private static IReadOnlyList<TimeBlock> ApplyAvailabilityBlocks(
        IReadOnlyList<TimeBlock> baseBlocks,
        IReadOnlyList<BusinessAvailabilityBlock> blocks)
    {
        if (blocks.Count == 0)
            return baseBlocks.Where(b => b.IsValid()).ToList();

        var intervals = baseBlocks
            .Where(b => b.IsValid())
            .Select(b => (Start: b.OpenTime, End: b.CloseTime))
            .ToList();

        foreach (var block in blocks)
        {
            if (intervals.Count == 0)
                break;

            if (!block.StartTime.HasValue || !block.EndTime.HasValue)
            {
                intervals.Clear();
                break;
            }

            var blockStart = block.StartTime.Value;
            var blockEnd = block.EndTime.Value;
            if (blockEnd <= blockStart)
                continue;

            intervals = SubtractBlock(intervals, blockStart, blockEnd);
        }

        return intervals
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .Select(i => ToTimeBlock(i.Start, i.End))
            .ToList();
    }

    private static List<(TimeSpan Start, TimeSpan End)> SubtractBlock(
        IReadOnlyList<(TimeSpan Start, TimeSpan End)> intervals,
        TimeSpan blockStart,
        TimeSpan blockEnd)
    {
        var result = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var interval in intervals)
        {
            if (blockEnd <= interval.Start || blockStart >= interval.End)
            {
                result.Add(interval);
                continue;
            }

            if (blockStart > interval.Start)
                result.Add((interval.Start, blockStart));

            if (blockEnd < interval.End)
                result.Add((blockEnd, interval.End));
        }

        return result;
    }

    private static TimeBlock ToTimeBlock(TimeSpan open, TimeSpan close) => new()
    {
        Open = open.ToString(@"hh\:mm"),
        Close = close.ToString(@"hh\:mm")
    };
}
