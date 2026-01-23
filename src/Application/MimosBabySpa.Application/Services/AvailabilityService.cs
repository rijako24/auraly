using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para verificar disponibilidad basándose únicamente en recursos disponibles y recursos usados por servicios.
/// Lógica simplificada: verifica si hay suficientes recursos físicos para el servicio solicitado.
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
        int? durationMinutes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Verificando disponibilidad: BusinessId={BusinessId}, Service={Service}, Date={Date}, Time={Time}, Duration={Duration}",
            businessId, service, date.ToString("yyyy-MM-dd"), time?.ToString(@"hh\:mm"), durationMinutes);

        var result = new AvailabilityResult
        {
            MaxCapacity = 1,
            BookedSlots = new List<BookedSlot>()
        };

        // Obtener el servicio desde la base de datos
        var requestedService = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, service);
        if (requestedService == null)
        {
            result.IsAvailable = false;
            result.Message = $"El servicio '{service}' no existe o no está activo.";
            _logger.LogWarning("Servicio '{Service}' no encontrado para negocio {BusinessId}", service, businessId);
            return result;
        }

        // Obtener todas las reservas del día completo
        var startOfDay = date.Date;
        var endOfDay = date.Date.AddDays(1).AddMinutes(-1);

        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId,
            startOfDay,
            endOfDay);

        // Filtrar solo reservas activas (no canceladas)
        var activeReservations = reservations
            .Where(r => r.Status != ReservationStatus.Cancelled)
            .ToList();

        // Construir lista de slots ocupados
        // Service debe estar cargado desde el repositorio
        result.BookedSlots = activeReservations
            .Select(r =>
            {
                var serviceName = r.Service?.ServiceName 
                    ?? throw new InvalidOperationException(
                        $"Service navigation property no está cargado para Reservation {r.ReservationId}. " +
                        "Asegúrate de usar Include(r => r.Service) al obtener las reservas.");
                
                return new BookedSlot
                {
                    Time = r.ReservationDateTime.ToString(@"HH\:mm"),
                    EndTime = r.ReservationDateTime.AddMinutes(r.DurationMinutes).ToString(@"HH\:mm"),
                    Duration = r.DurationMinutes,
                    Service = serviceName
                };
            })
            .ToList();

        result.CurrentReservations = result.BookedSlots.Count;

        // Si no se proporciona hora específica, solo retornar información del día
        if (!time.HasValue || !durationMinutes.HasValue)
        {
            result.IsAvailable = true;
            result.Message = result.CurrentReservations == 0
                ? "No hay reservas para esta fecha."
                : $"Hay {result.CurrentReservations} reserva(s) confirmada(s) para esta fecha.";
            
            _logger.LogInformation(
                "Disponibilidad verificada (sin hora específica): {IsAvailable}, Reservas={CurrentReservations}",
                result.IsAvailable, result.CurrentReservations);
            
            return result;
        }

        // Verificar solapamiento temporal con hora específica
        var requestedStartTime = time.Value;
        var requestedEndTime = requestedStartTime.Add(TimeSpan.FromMinutes(durationMinutes.Value));

        // Calcular slots que se solapan temporalmente
        var temporallyOverlappingSlots = result.BookedSlots
            .Where(slot =>
            {
                var slotStart = TimeSpan.Parse(slot.Time);
                var slotEnd = TimeSpan.Parse(slot.EndTime);
                bool overlaps = slotStart < requestedEndTime && slotEnd > requestedStartTime;
                return overlaps;
            })
            .ToList();

        result.OverlappingSlots = temporallyOverlappingSlots;

        // VALIDACIÓN PRIORITARIA: Verificar disponibilidad de empleados ANTES de recursos físicos
        // Si no hay personal disponible, no tiene sentido validar recursos físicos
        if (time.HasValue && durationMinutes.HasValue)
        {
            var reservationStartTime = date.Date.Add(requestedStartTime);
            var reservationEndTime = reservationStartTime.Add(TimeSpan.FromMinutes(durationMinutes.Value));
            
            var availableEmployee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
                businessId,
                requestedService.ServiceId,
                reservationStartTime,
                reservationEndTime,
                cancellationToken);

            if (availableEmployee == null)
            {
                result.IsAvailable = false;
                result.Message = $"El horario {requestedStartTime.ToString(@"hh\:mm")} NO está disponible. No hay personal disponible para este servicio en ese horario.";
                _logger.LogInformation("Disponibilidad rechazada por falta de personal disponible");
                return result;
            }

            _logger.LogDebug(
                "Empleado disponible encontrado: {EmployeeId} ({EmployeeName})",
                availableEmployee.EmployeeId, availableEmployee.Name);
        }

        // Si no hay solapamiento temporal y ya validamos empleados, está disponible
        if (temporallyOverlappingSlots.Count == 0)
        {
            result.IsAvailable = true;
            result.Message = $"El horario {requestedStartTime.ToString(@"hh\:mm")} está disponible. " +
                           (result.CurrentReservations > 0
                               ? $"Hay {result.CurrentReservations} reserva(s) en otros horarios del día."
                               : "No hay otras reservas para esta fecha.");
            
            _logger.LogInformation(
                "Disponibilidad verificada (sin solapamiento temporal): IsAvailable={IsAvailable}",
                result.IsAvailable);
            
            return result;
        }

        // Paso 1: Verificar recursos físicos (solo si hay solapamiento temporal)
        var hasEnoughResources = await CheckResourceAvailabilityAsync(
            businessId,
            requestedService.ServiceId,
            temporallyOverlappingSlots,
            activeReservations.Where(r =>
            {
                var rStart = r.ReservationDateTime.TimeOfDay;
                var rEnd = rStart.Add(TimeSpan.FromMinutes(r.DurationMinutes));
                return rStart < requestedEndTime && rEnd > requestedStartTime;
            }).ToList(),
            cancellationToken);

        if (!hasEnoughResources)
        {
            result.IsAvailable = false;
            result.Message = $"El horario {requestedStartTime.ToString(@"hh\:mm")} NO está disponible. No hay suficientes recursos físicos disponibles.";
            _logger.LogInformation("Disponibilidad rechazada por recursos físicos insuficientes");
            return result;
        }

        // Los empleados ya se validaron arriba antes de recursos físicos
        // Si llegamos aquí, tanto recursos físicos como personal están disponibles
        result.IsAvailable = true;
        result.Message = $"El horario {requestedStartTime.ToString(@"hh\:mm")} está disponible. " +
                       $"Hay {temporallyOverlappingSlots.Count} reserva(s) en el mismo horario pero hay recursos y personal suficientes.";

        _logger.LogInformation(
            "Disponibilidad verificada: IsAvailable={IsAvailable}, TemporallyOverlapping={OverlappingCount}, TotalReservations={TotalReservations}",
            result.IsAvailable, result.OverlappingSlots.Count, result.CurrentReservations);

        return result;
    }

    /// <summary>
    /// Verifica si hay recursos suficientes para el servicio solicitado considerando las reservas existentes en el mismo horario.
    /// </summary>
    private async Task<bool> CheckResourceAvailabilityAsync(
        Guid businessId,
        Guid requestedServiceId,
        List<BookedSlot> overlappingSlots,
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
