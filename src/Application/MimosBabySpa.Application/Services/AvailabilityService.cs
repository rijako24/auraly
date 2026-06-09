using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para verificar disponibilidad: empleado asignable + recursos suficientes.
///
/// Comportamiento:
///   - Con hora → revisa ese slot exacto (empleado + recursos).
///   - Sin hora → consulta el horario en <see cref="AvailabilityParams.Schedule"/>
///     y devuelve todos los slots del día disponibles.
///
/// Parámetros de agendamiento (intervalo, buffer, horario) se reciben como
/// <see cref="AvailabilityParams"/> desde el nodo que invoca la acción.
/// No se lee ninguna clave de BusinessConfigurations.
/// </summary>
public class AvailabilityService : IAvailabilityService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmployeeAssignmentService _employeeAssignmentService;
    private readonly IBusinessClock _businessClock;
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(
        IUnitOfWork unitOfWork,
        IEmployeeAssignmentService employeeAssignmentService,
        IBusinessClock businessClock,
        ILogger<AvailabilityService> logger)
    {
        _unitOfWork = unitOfWork;
        _employeeAssignmentService = employeeAssignmentService;
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

        _logger.LogInformation(
            "Verificando disponibilidad: BusinessId={BusinessId}, Service={Service}, Date={Date}, Time={Time}",
            businessId, service, date.ToString("yyyy-MM-dd"), time?.ToString(@"hh\:mm"));

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
            result.ResponseMessage = $"✗ No hay disponibilidad para {service} el {dateStr}. El servicio no existe o no está activo.";
            _logger.LogWarning("Servicio '{Service}' no encontrado para negocio {BusinessId}", service, businessId);
            return result;
        }

        var activeReservations = await LoadDayReservationsAsync(businessId, date, cancellationToken);
        result.CurrentReservations = activeReservations.Count;

        if (!time.HasValue)
        {
            result.AvailableTimeSlots = await GetAvailableSlotsForDayAsync(
                businessId, requestedService, date, activeReservations, effectivePolicy,
                businessToday, businessTimeOfDay, cancellationToken);
            result.IsAvailable = result.AvailableTimeSlots.Count > 0;
            result.ResponseMessage = result.IsAvailable
                ? $"✓ Disponibilidad confirmada para {service} el {dateStr}."
                : $"✗ No hay disponibilidad para {service} el {dateStr}. No hay horarios disponibles para ese día.";
            _logger.LogInformation(
                "Disponibilidad por día: {Date}, disponibles={Count}",
                dateStr, result.AvailableTimeSlots.Count);
            return result;
        }

        var requestedStartTime = time.Value;
        var isAvailable = await IsSlotAvailableAsync(
            businessId, requestedService, date, requestedStartTime,
            activeReservations, effectivePolicy, businessToday, businessTimeOfDay, cancellationToken);

        result.IsAvailable = isAvailable;

        if (isAvailable)
        {
            result.ResponseMessage = $"✓ Disponibilidad confirmada para {service} el {dateStr} a las {timeStr}. El horario está libre.";
        }
        else
        {
            result.AvailableTimeSlots = await GetAvailableSlotsForDayAsync(
                businessId, requestedService, date, activeReservations, effectivePolicy,
                businessToday, businessTimeOfDay, cancellationToken);
            result.ResponseMessage = result.AvailableTimeSlots.Count > 0
                ? $"✗ No hay disponibilidad para {service} el {dateStr} a las {timeStr}. Consulta otros horarios del día."
                : $"✗ No hay disponibilidad para {service} el {dateStr} a las {timeStr}. No hay horarios disponibles para ese día.";
            _logger.LogInformation(
                "Horario {TimeStr} no disponible; alternativas del día: {Count}",
                timeStr, result.AvailableTimeSlots.Count);
        }

        _logger.LogInformation("Disponibilidad verificada: IsAvailable={IsAvailable}", result.IsAvailable);
        return result;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ────────────────────────────────────────────────────────────────────────────

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
        var duration = requestedService.DurationMinutes;
        var candidateTimes = GetCandidateTimesFromSchedule(
            policy, date, duration, businessToday, businessTimeOfDay);
        var available = new List<string>();
        foreach (var startTime in candidateTimes)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (await IsSlotAvailableAsync(
                    businessId, requestedService, date, startTime, activeReservations, policy,
                    businessToday, businessTimeOfDay, cancellationToken))
                available.Add(startTime.ToString(@"hh\:mm"));
        }
        return available;
    }

    /// <summary>
    /// Genera los candidatos de horario a partir del Schedule en AvailabilityParams.
    /// Respeta el slotIntervalMinutes y excluye horarios pasados si es hoy.
    /// </summary>
    private List<TimeSpan> GetCandidateTimesFromSchedule(
        AvailabilityParams policy,
        DateTime date,
        int durationMinutes,
        DateOnly businessToday,
        TimeSpan businessTimeOfDay)
    {
        if (policy.Schedule == null || policy.Schedule.Count == 0)
        {
            _logger.LogWarning("AvailabilityParams.Schedule vacío — no se pueden generar candidatos de horario");
            return [];
        }

        var dayOfWeek = date.DayOfWeek.ToString().ToLower();
        if (!policy.Schedule.TryGetValue(dayOfWeek, out var timeBlocks) || timeBlocks == null || timeBlocks.Count == 0)
        {
            _logger.LogInformation("Sin horario configurado para {DayOfWeek} ({Date})", dayOfWeek, date.ToString("yyyy-MM-dd"));
            return [];
        }

        var candidates = new List<TimeSpan>();
        var interval = policy.SlotIntervalMinutes;

        foreach (var block in timeBlocks.Where(b => b.IsValid()))
        {
            var currentTime = block.OpenTime;
            var closeTime = block.CloseTime;
            while (currentTime.Add(TimeSpan.FromMinutes(durationMinutes)) <= closeTime)
            {
                candidates.Add(currentTime);
                currentTime = currentTime.Add(TimeSpan.FromMinutes(interval));
            }
        }

        if (DateOnly.FromDateTime(date) == businessToday)
            candidates.RemoveAll(t => t < businessTimeOfDay);

        _logger.LogDebug("Candidatos de horario para {Date}: {Count}", date.ToString("yyyy-MM-dd"), candidates.Count);
        return candidates;
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
        {
            _logger.LogDebug("Slot {StartTime} no disponible: hora ya pasada", startTime.ToString(@"hh\:mm"));
            return false;
        }

        var durationMinutes = requestedService.DurationMinutes + policy.BufferBetweenAppointmentsMinutes;
        var endTime = startTime.Add(TimeSpan.FromMinutes(durationMinutes));
        var reservationStart = date.Date.Add(startTime);
        var reservationEnd = reservationStart.Add(TimeSpan.FromMinutes(durationMinutes));

        if (policy.RequireEmployee)
        {
            var availableEmployee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
                businessId, requestedService.ServiceId, reservationStart, reservationEnd, cancellationToken);

            if (availableEmployee == null)
            {
                _logger.LogDebug("Slot {StartTime} no disponible: sin empleado", startTime.ToString(@"hh\:mm"));
                return false;
            }
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
        Guid businessId, DateTime date, CancellationToken cancellationToken)
    {
        var startOfDay = date.Date;
        var endOfDay = date.Date.AddDays(1).AddMinutes(-1);

        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId, startOfDay, endOfDay);

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
        {
            _logger.LogWarning("Servicio {ServiceId} no encontrado", requestedServiceId);
            return false;
        }

        var requestedResourceUsage = requestedService.ResourceUsages
            .ToDictionary(ru => ru.BusinessResource.ResourceName, ru => ru.Quantity);

        if (requestedResourceUsage.Count == 0)
        {
            _logger.LogWarning(
                "Servicio '{ServiceName}' sin ResourceUsages configurados pero hay {OverlapCount} reserva(s) solapada(s) en {SlotTime} — slot considerado disponible por defecto",
                requestedServiceName,
                overlappingReservations.Count,
                slotStartTime.ToString(@"hh\:mm"));
            return true;
        }

        var usedResources = new Dictionary<string, int>();
        var skippedOverlapCount = 0;
        foreach (var reservation in overlappingReservations)
        {
            if (!reservation.ServiceId.HasValue)
            {
                skippedOverlapCount++;
                continue;
            }

            var reservationService = reservation.Service
                ?? await _unitOfWork.Services.GetByIdAsync(reservation.ServiceId.Value);
            if (reservationService == null)
            {
                skippedOverlapCount++;
                continue;
            }

            foreach (var resourceUsage in reservationService.ResourceUsages)
            {
                var name = resourceUsage.BusinessResource.ResourceName;
                usedResources[name] = usedResources.TryGetValue(name, out var used) ? used + resourceUsage.Quantity : resourceUsage.Quantity;
            }
        }

        if (skippedOverlapCount > 0)
        {
            _logger.LogWarning(
                "Slot {SlotTime} para '{ServiceName}': {SkippedCount} reserva(s) solapada(s) sin ServiceId o servicio no encontrado — no aportan uso de recursos",
                slotStartTime.ToString(@"hh\:mm"),
                requestedServiceName,
                skippedOverlapCount);
        }

        var resourceSummaries = new List<string>();
        foreach (var (name, requiredQty) in requestedResourceUsage)
        {
            if (!availableResources.TryGetValue(name, out var totalAvailable))
            {
                _logger.LogWarning("Recurso '{ResourceName}' no encontrado en recursos del negocio", name);
                return false;
            }

            var currentlyUsed = usedResources.TryGetValue(name, out var used) ? used : 0;
            var remaining = totalAvailable - currentlyUsed - requiredQty;
            resourceSummaries.Add($"{name}: total={totalAvailable}, usado={currentlyUsed}, solicitado={requiredQty}, restante={remaining}");

            if (remaining < 0)
            {
                _logger.LogInformation(
                    "Recurso insuficiente en {SlotTime} para '{ServiceName}': '{ResourceName}'. Disponible: {Total}, Usado: {Used}, Solicitado: {Required}",
                    slotStartTime.ToString(@"hh\:mm"),
                    requestedServiceName,
                    name,
                    totalAvailable,
                    currentlyUsed,
                    requiredQty);
                return false;
            }
        }

        _logger.LogInformation(
            "Slot {SlotTime} disponible para '{ServiceName}' pese a {OverlapCount} reserva(s) solapada(s). Recursos: {ResourceSummary}",
            slotStartTime.ToString(@"hh\:mm"),
            requestedServiceName,
            overlappingReservations.Count,
            string.Join("; ", resourceSummaries));

        return true;
    }

    public async Task<Guid?> GetServiceIdByNameAsync(Guid businessId, string serviceName)
    {
        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, serviceName);
        return service?.ServiceId;
    }
}
