using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class AvailabilityService : IAvailabilityService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmployeeAssignmentService _employeeAssignmentService;
    private readonly IWorkingHoursService _workingHoursService;
    private readonly IBusinessClock _businessClock;
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(
        IUnitOfWork unitOfWork,
        IEmployeeAssignmentService employeeAssignmentService,
        IWorkingHoursService workingHoursService,
        IBusinessClock businessClock,
        ILogger<AvailabilityService> logger)
    {
        _unitOfWork = unitOfWork;
        _employeeAssignmentService = employeeAssignmentService;
        _workingHoursService = workingHoursService;
        _businessClock = businessClock;
        _logger = logger;
    }

    public async Task<AvailabilityResult> CheckAvailabilityAsync(
        Guid businessId,
        string service,
        DateTime date,
        TimeSpan? time,
        AvailabilityParams? policy = null,
        CancellationToken cancellationToken = default)
    {
        var effectivePolicy = policy ?? AvailabilityParams.Default;
        var clock = await _businessClock.GetSnapshotAsync(businessId, cancellationToken);
        var businessToday = clock.Today;
        var businessTimeOfDay = clock.Now.TimeOfDay;

        var dateStr = date.ToString("yyyy-MM-dd");
        var timeStr = time.HasValue ? FormatTime(time.Value) : null;

        var result = new AvailabilityResult
        {
            RequestServiceName = service,
            RequestDateString = dateStr,
            RequestTimeString = timeStr
        };

        var requestedService = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, service);
        if (requestedService == null)
        {
            result.IsAvailable = false;
            result.ResponseMessage = $"No hay disponibilidad para {service} el {dateStr}. El servicio no existe o no esta activo.";
            _logger.LogWarning("Servicio '{Service}' no encontrado para negocio {BusinessId}", service, businessId);
            return result;
        }

        var activeReservations = await LoadDayReservationsAsync(businessId, date, cancellationToken);
        result.CurrentReservations = activeReservations.Count;

        var dayAvailability = await GetAvailabilityForDayAsync(
            businessId,
            requestedService,
            date,
            activeReservations,
            effectivePolicy,
            businessToday,
            businessTimeOfDay,
            cancellationToken);

        result.AvailableWindows = dayAvailability.Windows.Select(ToDto).ToList();
        result.AvailableOptions = dayAvailability.Options.Select(ToDto).ToList();

        if (!time.HasValue)
        {
            result.IsAvailable = result.AvailableOptions.Count > 0;
            result.ResponseMessage = result.IsAvailable
                ? $"Disponibilidad confirmada para {service} el {dateStr}."
                : $"No hay disponibilidad para {service} el {dateStr}. No hay espacios disponibles para ese dia.";
            return result;
        }

        var requestedStart = time.Value;
        var serviceDuration = TimeSpan.FromMinutes(Math.Max(1, requestedService.DurationMinutes));
        result.RequestedOption = ToDto(new AvailabilityOptionRange(requestedStart, requestedStart.Add(serviceDuration)));

        var matchingOption = dayAvailability.Options
            .Where(option => option.Start == requestedStart)
            .Cast<AvailabilityOptionRange?>()
            .FirstOrDefault();
        if (matchingOption is { } option)
        {
            result.IsAvailable = true;
            result.Option = ToDto(option);
            result.ResponseMessage = $"Disponibilidad confirmada para {service} el {dateStr} a las {timeStr}. El horario esta libre.";
            return result;
        }

        result.IsAvailable = false;
        result.ResponseMessage = result.AvailableOptions.Count > 0
            ? $"No hay disponibilidad para {service} el {dateStr} a las {timeStr}. Consulta otros espacios del dia."
            : $"No hay disponibilidad para {service} el {dateStr} a las {timeStr}. No hay espacios disponibles para ese dia.";
        return result;
    }

    private async Task<(List<AvailabilityWindowRange> Windows, List<AvailabilityOptionRange> Options)> GetAvailabilityForDayAsync(
        Guid businessId,
        Domain.Entities.Service requestedService,
        DateTime date,
        List<Domain.Entities.Reservation> activeReservations,
        AvailabilityParams policy,
        DateOnly businessToday,
        TimeSpan businessTimeOfDay,
        CancellationToken cancellationToken)
    {
        var windows = await GetFreeWindowsForDayAsync(
            businessId,
            requestedService,
            date,
            activeReservations,
            policy,
            businessToday,
            businessTimeOfDay,
            cancellationToken);

        var options = new List<AvailabilityOptionRange>();
        foreach (var window in windows)
        {
            foreach (var option in GenerateOptions(window, requestedService.DurationMinutes, policy))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (await IsSlotAvailableAsync(
                        businessId,
                        requestedService,
                        date,
                        option.Start,
                        activeReservations,
                        policy,
                        businessToday,
                        businessTimeOfDay,
                        cancellationToken))
                {
                    options.Add(option);
                }
            }
        }

        options = options
            .GroupBy(option => option.Start)
            .Select(group => group.OrderBy(option => option.End).First())
            .OrderBy(option => option.Start)
            .ThenBy(option => option.End)
            .ToList();

        var usableWindows = MergeWindows(
            windows.Where(window => options.Any(option => option.Start >= window.Start && option.End <= window.End)));

        return (usableWindows, options);
    }

    private async Task<List<AvailabilityWindowRange>> GetFreeWindowsForDayAsync(
        Guid businessId,
        Domain.Entities.Service requestedService,
        DateTime date,
        List<Domain.Entities.Reservation> activeReservations,
        AvailabilityParams policy,
        DateOnly businessToday,
        TimeSpan businessTimeOfDay,
        CancellationToken cancellationToken)
    {
        var dateOnly = DateOnly.FromDateTime(date);
        var windows = new List<AvailabilityWindowRange>();

        if (policy.RequireEmployee)
        {
            var employees = await _unitOfWork.Employees.GetByBusinessIdAndServiceIdAsync(businessId, requestedService.ServiceId);
            foreach (var employee in employees)
            {
                var employeeBlocks = await _workingHoursService.GetEffectiveWorkingHoursAsync(
                    businessId,
                    employee.EmployeeId,
                    dateOnly,
                    cancellationToken);

                var employeeReservations = activeReservations
                    .Where(r => r.EmployeeId == employee.EmployeeId)
                    .ToList();

                windows.AddRange(BuildFreeWindows(employeeBlocks, employeeReservations, policy));
            }
        }
        else
        {
            var businessBlocks = await _workingHoursService.GetEffectiveBusinessWorkingHoursAsync(
                businessId,
                dateOnly,
                cancellationToken);

            windows.AddRange(BuildFreeWindows(businessBlocks, activeReservations, policy));
        }

        if (dateOnly == businessToday)
        {
            var earliestStart = businessTimeOfDay.Add(TimeSpan.FromMinutes(Math.Max(0, policy.MinimumLeadTimeMinutes)));
            windows = TrimWindowsBefore(windows, earliestStart);
        }

        var requiredDuration = TimeSpan.FromMinutes(Math.Max(1, requestedService.DurationMinutes) + Math.Max(0, policy.BufferBetweenAppointmentsMinutes));
        return MergeWindows(windows)
            .Where(window => window.End - window.Start >= requiredDuration)
            .OrderBy(window => window.Start)
            .ThenBy(window => window.End)
            .ToList();
    }

    private static List<AvailabilityWindowRange> BuildFreeWindows(
        IReadOnlyList<TimeBlock> workingBlocks,
        IReadOnlyList<Domain.Entities.Reservation> reservations,
        AvailabilityParams policy)
    {
        var windows = workingBlocks
            .Where(block => block.IsValid())
            .Select(block => new AvailabilityWindowRange(block.OpenTime, block.CloseTime))
            .ToList();

        foreach (var reservation in reservations
            .Where(r => r.ReservationDateTime.HasValue && r.DurationMinutes.HasValue)
            .OrderBy(r => r.ReservationDateTime!.Value))
        {
            var start = reservation.ReservationDateTime!.Value.TimeOfDay;
            var duration = Math.Max(1, reservation.DurationMinutes!.Value) + Math.Max(0, policy.BufferBetweenAppointmentsMinutes);
            var end = start.Add(TimeSpan.FromMinutes(duration));
            windows = SubtractRange(windows, start, end);
        }

        return windows;
    }

    private static List<AvailabilityOptionRange> GenerateOptions(
        AvailabilityWindowRange window,
        int serviceDurationMinutes,
        AvailabilityParams policy)
    {
        var serviceDuration = TimeSpan.FromMinutes(Math.Max(1, serviceDurationMinutes));
        var blockingDuration = TimeSpan.FromMinutes(Math.Max(1, serviceDurationMinutes) + Math.Max(0, policy.BufferBetweenAppointmentsMinutes));
        var latestStart = window.End - blockingDuration;
        if (latestStart < window.Start)
            return [];

        var starts = new HashSet<TimeSpan> { window.Start, latestStart };
        var interval = Math.Max(1, policy.SlotIntervalMinutes);
        var current = AlignUp(window.Start, interval);
        while (current <= latestStart)
        {
            starts.Add(current);
            current = current.Add(TimeSpan.FromMinutes(interval));
        }

        return starts
            .Where(start => start >= window.Start && start <= latestStart)
            .OrderBy(start => start)
            .Select(start => new AvailabilityOptionRange(start, start.Add(serviceDuration)))
            .ToList();
    }

    private static TimeSpan AlignUp(TimeSpan value, int intervalMinutes)
    {
        var intervalTicks = TimeSpan.FromMinutes(intervalMinutes).Ticks;
        if (intervalTicks <= 0)
            return value;

        var remainder = value.Ticks % intervalTicks;
        return remainder == 0
            ? value
            : TimeSpan.FromTicks(value.Ticks + intervalTicks - remainder);
    }

    private static List<AvailabilityWindowRange> TrimWindowsBefore(
        IEnumerable<AvailabilityWindowRange> windows,
        TimeSpan earliestStart)
    {
        return windows
            .Select(window => new AvailabilityWindowRange(Max(window.Start, earliestStart), window.End))
            .Where(window => window.End > window.Start)
            .ToList();
    }

    private static List<AvailabilityWindowRange> SubtractRange(
        IReadOnlyList<AvailabilityWindowRange> windows,
        TimeSpan blockStart,
        TimeSpan blockEnd)
    {
        if (blockEnd <= blockStart)
            return windows.ToList();

        var result = new List<AvailabilityWindowRange>();
        foreach (var window in windows)
        {
            if (blockEnd <= window.Start || blockStart >= window.End)
            {
                result.Add(window);
                continue;
            }

            if (blockStart > window.Start)
                result.Add(new AvailabilityWindowRange(window.Start, Min(blockStart, window.End)));

            if (blockEnd < window.End)
                result.Add(new AvailabilityWindowRange(Max(blockEnd, window.Start), window.End));
        }

        return result.Where(window => window.End > window.Start).ToList();
    }

    private static List<AvailabilityWindowRange> MergeWindows(IEnumerable<AvailabilityWindowRange> windows)
    {
        var ordered = windows
            .Where(window => window.End > window.Start)
            .OrderBy(window => window.Start)
            .ThenBy(window => window.End)
            .ToList();

        if (ordered.Count == 0)
            return [];

        var merged = new List<AvailabilityWindowRange> { ordered[0] };
        foreach (var window in ordered.Skip(1))
        {
            var last = merged[^1];
            if (window.Start <= last.End)
            {
                merged[^1] = new AvailabilityWindowRange(last.Start, Max(last.End, window.End));
                continue;
            }

            merged.Add(window);
        }

        return merged;
    }

    private async Task<bool> IsSlotAvailableAsync(
        Guid businessId,
        Domain.Entities.Service requestedService,
        DateTime date,
        TimeSpan startTime,
        List<Domain.Entities.Reservation> activeReservations,
        AvailabilityParams policy,
        DateOnly businessToday,
        TimeSpan businessTimeOfDay,
        CancellationToken cancellationToken)
    {
        if (DateOnly.FromDateTime(date) == businessToday)
        {
            var earliestStart = businessTimeOfDay.Add(TimeSpan.FromMinutes(Math.Max(0, policy.MinimumLeadTimeMinutes)));
            if (startTime < earliestStart)
                return false;
        }

        var durationMinutes = Math.Max(1, requestedService.DurationMinutes) + Math.Max(0, policy.BufferBetweenAppointmentsMinutes);
        var endTime = startTime.Add(TimeSpan.FromMinutes(durationMinutes));
        var reservationStart = date.Date.Add(startTime);
        var reservationEnd = reservationStart.Add(TimeSpan.FromMinutes(durationMinutes));

        if (policy.RequireEmployee)
        {
            var availableEmployee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
                businessId,
                requestedService.ServiceId,
                reservationStart,
                reservationEnd,
                cancellationToken);

            if (availableEmployee == null)
                return false;
        }

        var overlappingReservations = GetOverlappingReservations(activeReservations, startTime, endTime, policy);
        if (overlappingReservations.Count == 0)
            return true;

        return await CheckResourceAvailabilityAsync(
            businessId,
            requestedService.ServiceId,
            requestedService.ServiceName,
            startTime,
            overlappingReservations,
            cancellationToken);
    }

    private static List<Domain.Entities.Reservation> GetOverlappingReservations(
        List<Domain.Entities.Reservation> activeReservations,
        TimeSpan startTime,
        TimeSpan endTime,
        AvailabilityParams policy)
    {
        return activeReservations
            .Where(r => r.ReservationDateTime.HasValue && r.DurationMinutes.HasValue)
            .Where(r =>
            {
                var rStart = r.ReservationDateTime!.Value.TimeOfDay;
                var rEnd = rStart.Add(TimeSpan.FromMinutes(Math.Max(1, r.DurationMinutes!.Value) + Math.Max(0, policy.BufferBetweenAppointmentsMinutes)));
                return rStart < endTime && rEnd > startTime;
            })
            .ToList();
    }

    private async Task<List<Domain.Entities.Reservation>> LoadDayReservationsAsync(
        Guid businessId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var startOfDay = date.Date;
        var endOfDay = date.Date.AddDays(1).AddMinutes(-1);

        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId,
            startOfDay,
            endOfDay);

        return reservations
            .Where(r => r.ReservationDateTime.HasValue && r.Status.BlocksAvailability())
            .ToList();
    }

    private async Task<bool> CheckResourceAvailabilityAsync(
        Guid businessId,
        Guid requestedServiceId,
        string requestedServiceName,
        TimeSpan slotStartTime,
        List<Domain.Entities.Reservation> overlappingReservations,
        CancellationToken cancellationToken)
    {
        var businessResources = await _unitOfWork.BusinessResources.GetByBusinessIdAsync(businessId);
        var availableResources = businessResources.ToDictionary(r => r.ResourceName, r => r.Quantity);

        var requestedService = await _unitOfWork.Services.GetByIdAsync(requestedServiceId);
        if (requestedService == null)
            return false;

        var requestedResourceUsage = requestedService.ResourceUsages
            .ToDictionary(ru => ru.BusinessResource.ResourceName, ru => ru.Quantity);

        if (requestedResourceUsage.Count == 0)
            return true;

        var usedResources = new Dictionary<string, int>();
        foreach (var reservation in overlappingReservations)
        {
            if (!reservation.ServiceId.HasValue)
                continue;

            var reservationService = reservation.Service
                ?? await _unitOfWork.Services.GetByIdAsync(reservation.ServiceId.Value);
            if (reservationService == null)
                continue;

            foreach (var resourceUsage in reservationService.ResourceUsages)
            {
                var name = resourceUsage.BusinessResource.ResourceName;
                usedResources[name] = usedResources.TryGetValue(name, out var used) ? used + resourceUsage.Quantity : resourceUsage.Quantity;
            }
        }

        foreach (var (name, requiredQty) in requestedResourceUsage)
        {
            if (!availableResources.TryGetValue(name, out var totalAvailable))
                return false;

            var currentlyUsed = usedResources.TryGetValue(name, out var used) ? used : 0;
            if (totalAvailable - currentlyUsed - requiredQty < 0)
            {
                _logger.LogInformation(
                    "Recurso insuficiente en {SlotTime} para '{ServiceName}': '{ResourceName}'",
                    FormatTime(slotStartTime),
                    requestedServiceName,
                    name);
                return false;
            }
        }

        return true;
    }

    public async Task<Guid?> GetServiceIdByNameAsync(Guid businessId, string serviceName)
    {
        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, serviceName);
        return service?.ServiceId;
    }

    private static AvailabilityWindow ToDto(AvailabilityWindowRange window) =>
        new(FormatTime(window.Start), FormatTime(window.End));

    private static AvailabilityOption ToDto(AvailabilityOptionRange option) =>
        new(FormatTime(option.Start), FormatTime(option.End));

    private static string FormatTime(TimeSpan time) => time.ToString(@"hh\:mm");

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
}