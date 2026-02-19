using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para verificar disponibilidad: empleado asignable + recursos suficientes.
/// Dos comportamientos: con hora revisa ese slot; sin hora consulta el horario del negocio y revisa cada hora abierta.
/// </summary>
public class AvailabilityService : IAvailabilityService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmployeeAssignmentService _employeeAssignmentService;
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(
        IUnitOfWork unitOfWork,
        IEmployeeAssignmentService employeeAssignmentService,
        ILogger<AvailabilityService> logger)
    {
        _unitOfWork = unitOfWork;
        _employeeAssignmentService = employeeAssignmentService;
        _logger = logger;
    }

    public async Task<AvailabilityResult> CheckAvailabilityAsync(
        Guid businessId,
        string service,
        DateTime date,
        TimeSpan? time,
        CancellationToken cancellationToken = default)
    {
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
            result.AvailableTimeSlots = await GetAvailableSlotsForDayAsync(businessId, requestedService, date, activeReservations, cancellationToken);
            result.IsAvailable = result.AvailableTimeSlots.Count > 0;
            result.ResponseMessage = result.IsAvailable
                ? $"✓ Disponibilidad confirmada para {service} el {dateStr}. Horarios disponibles: {string.Join(",", result.AvailableTimeSlots)}."
                : $"✗ No hay disponibilidad para {service} el {dateStr}. No hay horarios disponibles para ese día.";
            _logger.LogInformation(
                "Disponibilidad por día: {Date}, disponibles={Count}",
                dateStr, result.AvailableTimeSlots.Count);
            return result;
        }

        var requestedStartTime = time.Value;
        var isAvailable = await IsSlotAvailableAsync(
            businessId,
            requestedService,
            date,
            requestedStartTime,
            activeReservations,
            cancellationToken);

        result.IsAvailable = isAvailable;

        if (isAvailable)
        {
            result.ResponseMessage = $"✓ Disponibilidad confirmada para {service} el {dateStr} a las {timeStr}. El horario está libre.";
        }
        else
        {
            // El horario solicitado no está disponible: consultar alternativas del día
            // para que el bot pueda mostrarlas en lugar de inventar horarios
            result.AvailableTimeSlots = await GetAvailableSlotsForDayAsync(businessId, requestedService, date, activeReservations, cancellationToken);
            result.ResponseMessage = result.AvailableTimeSlots.Count > 0
                ? $"✗ No hay disponibilidad para {service} el {dateStr} a las {timeStr}. Horarios disponibles: {string.Join(", ", result.AvailableTimeSlots)}."
                : $"✗ No hay disponibilidad para {service} el {dateStr} a las {timeStr}. No hay horarios disponibles para ese día.";
            _logger.LogInformation(
                "Horario {TimeStr} no disponible; alternativas del día: {Count}",
                timeStr, result.AvailableTimeSlots.Count);
        }

        _logger.LogInformation("Disponibilidad verificada: IsAvailable={IsAvailable}", result.IsAvailable);
        return result;
    }

    /// <summary>
    /// Obtiene los horarios realmente disponibles en el día: consulta horario del negocio e itera hora por hora (empleado + recursos).
    /// La duración del slot se obtiene del servicio.
    /// </summary>
    private async Task<List<string>> GetAvailableSlotsForDayAsync(
        Guid businessId,
        Domain.Entities.Service requestedService,
        DateTime date,
        List<Domain.Entities.Reservation> activeReservations,
        CancellationToken cancellationToken)
    {
        var duration = requestedService.DurationMinutes;
        var candidateTimes = await GetCandidateTimesFromBusinessScheduleAsync(businessId, date, duration, cancellationToken);
        var available = new List<string>();
        foreach (var startTime in candidateTimes)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (await IsSlotAvailableAsync(businessId, requestedService, date, startTime, activeReservations, cancellationToken))
                available.Add(startTime.ToString(@"hh\:mm"));
        }
        return available;
    }

    private async Task<List<TimeSpan>> GetCandidateTimesFromBusinessScheduleAsync(
        Guid businessId,
        DateTime date,
        int durationMinutes,
        CancellationToken cancellationToken)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId);
        if (business == null || string.IsNullOrWhiteSpace(business.OperatingHoursJson) || business.OperatingHoursJson == "{}")
        {
            _logger.LogWarning("Negocio {BusinessId} sin horario configurado", businessId);
            return new List<TimeSpan>();
        }

        var schedule = DeserializeSchedule(business.OperatingHoursJson);
        var dayOfWeek = date.DayOfWeek.ToString().ToLower();
        if (!schedule.TryGetValue(dayOfWeek, out var timeBlocks) || timeBlocks == null || !timeBlocks.Any())
        {
            _logger.LogInformation("Negocio cerrado el {DayOfWeek} ({Date})", dayOfWeek, date.ToString("yyyy-MM-dd"));
            return new List<TimeSpan>();
        }

        var candidates = new List<TimeSpan>();
        const int slotIntervalMinutes = 60;
        foreach (var block in timeBlocks.Where(b => b.IsValid()))
        {
            var currentTime = block.OpenTime;
            var closeTime = block.CloseTime;
            while (currentTime.Add(TimeSpan.FromMinutes(durationMinutes)) <= closeTime)
            {
                candidates.Add(currentTime);
                currentTime = currentTime.Add(TimeSpan.FromMinutes(slotIntervalMinutes));
            }
        }
        _logger.LogDebug("Candidatos de horario para {Date}: {Count}", date.ToString("yyyy-MM-dd"), candidates.Count);
        return candidates;
    }

    private static Dictionary<string, List<TimeBlock>> DeserializeSchedule(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}") return new Dictionary<string, List<TimeBlock>>();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Dictionary<string, List<TimeBlock>>>(json, options) ?? new Dictionary<string, List<TimeBlock>>();
        }
        catch { return new Dictionary<string, List<TimeBlock>>(); }
    }

    /// <summary>
    /// Comprueba si un slot está disponible: empleado asignable + recursos suficientes (si hay solapamiento).
    /// La duración se obtiene del servicio solicitado.
    /// </summary>
    private async Task<bool> IsSlotAvailableAsync(
        Guid businessId,
        Domain.Entities.Service requestedService,
        DateTime date,
        TimeSpan startTime,
        List<Domain.Entities.Reservation> activeReservations,
        CancellationToken cancellationToken)
    {
        var durationMinutes = requestedService.DurationMinutes;
        var endTime = startTime.Add(TimeSpan.FromMinutes(durationMinutes));
        var reservationStart = date.Date.Add(startTime);
        var reservationEnd = reservationStart.Add(TimeSpan.FromMinutes(durationMinutes));

        var availableEmployee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
            businessId,
            requestedService.ServiceId,
            reservationStart,
            reservationEnd,
            cancellationToken);

        if (availableEmployee == null)
        {
            _logger.LogDebug("Slot {StartTime} no disponible: sin empleado", startTime.ToString(@"hh\:mm"));
            return false;
        }

        var overlappingReservations = GetOverlappingReservations(activeReservations, startTime, endTime);
        if (overlappingReservations.Count == 0)
            return true;

        return await CheckResourceAvailabilityAsync(
            businessId,
            requestedService.ServiceId,
            overlappingReservations,
            cancellationToken);
    }

    private static List<Domain.Entities.Reservation> GetOverlappingReservations(
        List<Domain.Entities.Reservation> activeReservations,
        TimeSpan startTime,
        TimeSpan endTime)
    {
        return activeReservations
            .Where(r =>
            {
                var rStart = r.ReservationDateTime.TimeOfDay;
                var rEnd = rStart.Add(TimeSpan.FromMinutes(r.DurationMinutes));
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
            .Where(r => r.Status != ReservationStatus.Cancelled)
            .ToList();
    }

    /// <summary>
    /// Verifica si hay recursos suficientes para el servicio solicitado considerando las reservas existentes en el mismo horario.
    /// </summary>
    private async Task<bool> CheckResourceAvailabilityAsync(
        Guid businessId,
        Guid requestedServiceId,
        List<Domain.Entities.Reservation> overlappingReservations,
        CancellationToken cancellationToken)
    {
        // Obtener recursos disponibles del negocio
        var businessResources = await _unitOfWork.BusinessResources.GetByBusinessIdAsync(businessId);
        var availableResources = businessResources.ToDictionary(
            r => r.ResourceName,
            r => r.Quantity);

        // Obtener uso de recursos del servicio solicitado
        var requestedService = await _unitOfWork.Services.GetByIdAsync(requestedServiceId);
        if (requestedService == null)
        {
            _logger.LogWarning("Servicio {ServiceId} no encontrado", requestedServiceId);
            return false;
        }

        var requestedResourceUsage = requestedService.ResourceUsages.ToDictionary(
            ru => ru.BusinessResource.ResourceName,
            ru => ru.Quantity);

        // Calcular recursos usados por las reservas existentes en el mismo horario
        var usedResources = new Dictionary<string, int>();

        foreach (var reservation in overlappingReservations)
        {
            // Obtener el servicio de la reserva (cargar si es necesario)
            var reservationService = reservation.Service ?? 
                await _unitOfWork.Services.GetByIdAsync(reservation.ServiceId);

            if (reservationService == null)
            {
                _logger.LogWarning("Servicio {ServiceId} de reserva {ReservationId} no encontrado", 
                    reservation.ServiceId, reservation.ReservationId);
                continue;
            }

            // Sumar recursos usados por esta reserva
            foreach (var resourceUsage in reservationService.ResourceUsages)
            {
                var resourceName = resourceUsage.BusinessResource.ResourceName;
                if (usedResources.ContainsKey(resourceName))
                {
                    usedResources[resourceName] += resourceUsage.Quantity;
                }
                else
                {
                    usedResources[resourceName] = resourceUsage.Quantity;
                }
            }
        }

        // Verificar si hay recursos suficientes para el servicio solicitado
        foreach (var requestedResource in requestedResourceUsage)
        {
            var resourceName = requestedResource.Key;
            var requestedQuantity = requestedResource.Value;

            // Obtener cantidad disponible del recurso
            if (!availableResources.TryGetValue(resourceName, out var totalAvailable))
            {
                _logger.LogWarning("Recurso '{ResourceName}' no encontrado en recursos disponibles del negocio", resourceName);
                return false;
            }

            // Calcular cantidad usada actualmente
            var currentlyUsed = usedResources.TryGetValue(resourceName, out var used) ? used : 0;

            // Calcular cantidad disponible después de agregar el servicio solicitado
            var availableAfterRequest = totalAvailable - currentlyUsed - requestedQuantity;

            if (availableAfterRequest < 0)
            {
                _logger.LogInformation(
                    "Recurso insuficiente: '{ResourceName}'. Disponible: {TotalAvailable}, Usado: {CurrentlyUsed}, Solicitado: {RequestedQuantity}",
                    resourceName, totalAvailable, currentlyUsed, requestedQuantity);
                return false;
            }
        }

        // Todos los recursos están disponibles
        return true;
    }

    /// <summary>
    /// Obtiene el ServiceId desde el nombre del servicio
    /// </summary>
    public async Task<Guid?> GetServiceIdByNameAsync(Guid businessId, string serviceName)
    {
        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, serviceName);
        return service?.ServiceId;
    }
}
