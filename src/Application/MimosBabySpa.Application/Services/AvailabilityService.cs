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
        var timeStr = time.HasValue ? time.Value.ToString(@"hh\:mm") : null;

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

        if (!time.HasValue)
        {
            result.AvailableTimeSlots = await GetAvailableSlotsForDayAsync(
                businessId,
                requestedService,
                date,
                activeReservations,
                effectivePolicy,
                businessToday,
                businessTimeOfDay,
                cancellationToken);

            result.IsAvailable = result.AvailableTimeSlots.Count > 0;
            result.ResponseMessage = result.IsAvailable
                ? $"Disponibilidad confirmada para {service} el {dateStr}."
                : $"No hay disponibilidad para {service} el {dateStr}. No hay horarios disponibles para ese dia.";
            return result;
        }

        var requestedStartTime = time.Value;
        result.IsAvailable = await IsSlotAvailableAsync(
            businessId,
            requestedService,
            date,
            requestedStartTime,
            activeReservations,
            effectivePolicy,
            businessToday,
            businessTimeOfDay,
            cancellationToken);

        if (result.IsAvailable)
        {
            result.ResponseMessage = $"Disponibilidad confirmada para {service} el {dateStr} a las {timeStr}. El horario esta libre.";
        }
        else
        {
            result.AvailableTimeSlots = await GetAvailableSlotsForDayAsync(
                businessId,
                requestedService,
                date,
                activeReservations,
                effectivePolicy,
                businessToday,
                businessTimeOfDay,
                cancellationToken);
            result.ResponseMessage = result.AvailableTimeSlots.Count > 0
                ? $"No hay disponibilidad para {service} el {dateStr} a las {timeStr}. Consulta otros horarios del dia."
                : $"No hay disponibilidad para {service} el {dateStr} a las {timeStr}. No hay horarios disponibles para ese dia.";
        }

        return result;
    }

    private async Task<List<string>> GetAvailableSlotsForDayAsync(
        Guid businessId,
        Domain.Entities.Service requestedService,
        DateTime date,
        List<Domain.Entities.Reservation> activeReservations,
        AvailabilityParams policy,
        DateOnly businessToday,
        TimeSpan businessTimeOfDay,
        CancellationToken cancellationToken)
    {
        var candidateTimes = await GetCandidateTimesFromWorkingHoursAsync(
            businessId,
            requestedService.ServiceId,
            policy,
            date,
            requestedService.DurationMinutes,
            businessToday,
            businessTimeOfDay,
            cancellationToken);

        var available = new List<string>();
        foreach (var startTime in candidateTimes)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (await IsSlotAvailableAsync(
                    businessId,
                    requestedService,
                    date,
                    startTime,
                    activeReservations,
                    policy,
                    businessToday,
                    businessTimeOfDay,
                    cancellationToken))
            {
                available.Add(startTime.ToString(@"hh\:mm"));
            }
        }

        return available;
    }

    private async Task<List<TimeSpan>> GetCandidateTimesFromWorkingHoursAsync(
        Guid businessId,
        Guid serviceId,
        AvailabilityParams policy,
        DateTime date,
        int durationMinutes,
        DateOnly businessToday,
        TimeSpan businessTimeOfDay,
        CancellationToken cancellationToken)
    {
        var blocks = new List<TimeBlock>();

        if (policy.RequireEmployee)
        {
            var employees = await _unitOfWork.Employees.GetByBusinessIdAndServiceIdAsync(businessId, serviceId);
            foreach (var employee in employees)
            {
                var employeeBlocks = await _workingHoursService.GetEffectiveWorkingHoursAsync(
                    businessId,
                    employee.EmployeeId,
                    DateOnly.FromDateTime(date),
                    cancellationToken);
                blocks.AddRange(employeeBlocks);
            }
        }
        else
        {
            blocks.AddRange(await _workingHoursService.GetEffectiveBusinessWorkingHoursAsync(
                businessId,
                DateOnly.FromDateTime(date),
                cancellationToken));
        }

        if (blocks.Count == 0)
            return [];

        var interval = Math.Max(1, policy.SlotIntervalMinutes);
        var candidates = new HashSet<TimeSpan>();

        foreach (var block in blocks.Where(b => b.IsValid()))
        {
            var currentTime = block.OpenTime;
            while (currentTime.Add(TimeSpan.FromMinutes(durationMinutes)) <= block.CloseTime)
            {
                candidates.Add(currentTime);
                currentTime = currentTime.Add(TimeSpan.FromMinutes(interval));
            }
        }

        var ordered = candidates.OrderBy(t => t).ToList();
        if (DateOnly.FromDateTime(date) == businessToday)
            ordered.RemoveAll(t => t < businessTimeOfDay);

        return ordered;
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
        if (DateOnly.FromDateTime(date) == businessToday && startTime < businessTimeOfDay)
            return false;

        var durationMinutes = requestedService.DurationMinutes + policy.BufferBetweenAppointmentsMinutes;
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

        var overlappingReservations = GetOverlappingReservations(activeReservations, startTime, endTime);
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
        TimeSpan endTime)
    {
        return activeReservations
            .Where(r => r.ReservationDateTime.HasValue && r.DurationMinutes.HasValue)
            .Where(r =>
            {
                var rStart = r.ReservationDateTime!.Value.TimeOfDay;
                var rEnd = rStart.Add(TimeSpan.FromMinutes(r.DurationMinutes!.Value));
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
                    slotStartTime.ToString(@"hh\:mm"),
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
}
