using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ReservationService : IReservationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICalendarService _calendarService;
    private readonly IIntegrationsConfigProvider _integrationsProvider;
    private readonly IEmployeeAssignmentService _employeeAssignmentService;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(
        IUnitOfWork unitOfWork,
        ICalendarService calendarService,
        IIntegrationsConfigProvider integrationsProvider,
        IEmployeeAssignmentService employeeAssignmentService,
        ILogger<ReservationService> logger)
    {
        _unitOfWork = unitOfWork;
        _calendarService = calendarService;
        _integrationsProvider = integrationsProvider;
        _employeeAssignmentService = employeeAssignmentService;
        _logger = logger;
    }

    public async Task<CreateReservationResponse> CreateReservationAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validar negocio
        var business = await _unitOfWork.Businesses.GetByIdAsync(request.BusinessId);
        if (business == null)
        {
            throw new InvalidOperationException($"El negocio con ID {request.BusinessId} no existe.");
        }

        // 2. Resolver servicio por nombre
        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(
            request.BusinessId, request.ServiceName);
        if (service == null)
        {
            throw new InvalidOperationException($"El servicio '{request.ServiceName}' no existe en el sistema.");
        }

        var duration = service.DurationMinutes > 0 ? service.DurationMinutes : 60;
        var reservationDateTime = request.Date.ToDateTime(request.Time);

        // 3. Obtener empleado disponible
        var endTime = reservationDateTime.AddMinutes(duration);
        var employee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
            request.BusinessId,
            service.ServiceId,
            reservationDateTime,
            endTime,
            cancellationToken);

        if (employee == null)
        {
            throw new InvalidOperationException(
                "No hay empleado disponible para este horario. Por favor intenta con otra fecha u hora.");
        }

        // 4. Resolver add-ons (nombres CSV → IDs + entidades para PriceSnapshot)
        var (addOnServiceIds, addOnNames) = await ResolveAddOnsAsync(
            request.BusinessId,
            request.SelectedAddOnsCsv,
            cancellationToken);

        // 5. Construir metadata para calendario
        var metadata = BuildMetadata(request);

        // 6. Crear entidad Reservation (tracking, sin SaveChanges aún)
        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = request.BusinessId,
            ServiceId = service.ServiceId,
            EmployeeId = employee.EmployeeId,
            ReservationDateTime = reservationDateTime,
            DurationMinutes = duration,
            Status = ReservationStatus.Confirmed,
            ConversationId = request.ConversationId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createdReservation = await _unitOfWork.Reservations.CreateAsync(reservation);

        // 7. Agregar ReservationAddOns (tracking, sin SaveChanges)
        foreach (var (addOnServiceId, addOnService) in addOnServiceIds)
        {
            var reservationAddOn = new ReservationAddOn
            {
                ReservationAddOnId = Guid.NewGuid(),
                ReservationId = createdReservation.ReservationId,
                AddOnServiceId = addOnServiceId,
                PriceSnapshot = addOnService.Price
            };
            await _unitOfWork.ReservationAddOns.AddAsync(reservationAddOn);
        }

        // 8. Un solo SaveChanges — atomicidad reserva + add-ons
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 9. Sincronizar con calendario (best-effort, después de persistir)
        if (await IsCalendarSyncEnabledAsync(request.BusinessId, cancellationToken))
        {
            var serviceName = service.ServiceName;
            var calendarEventId = await TrySyncReservationToCalendarAsync(
                createdReservation,
                serviceName,
                metadata,
                cancellationToken);
            if (calendarEventId != null)
            {
                createdReservation.CalendarEventId = calendarEventId;
                await _unitOfWork.Reservations.UpdateAsync(createdReservation);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            _logger.LogDebug(
                "Calendar sync deshabilitado para BusinessId={BusinessId}",
                request.BusinessId);
        }

        _logger.LogInformation(
            "Reserva creada exitosamente: {ReservationId} para servicio {ServiceName} el {DateTime}",
            createdReservation.ReservationId,
            service.ServiceName,
            createdReservation.ReservationDateTime);

        return new CreateReservationResponse(
            createdReservation.ReservationId,
            service.ServiceName,
            employee.Name,
            request.Date,
            request.Time,
            duration,
            addOnNames);
    }

    public async Task<ReservationDto?> GetReservationByIdAsync(Guid reservationId)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
        return reservation != null ? MapToDto(reservation) : null;
    }

    public async Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAsync(Guid businessId)
    {
        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAsync(businessId);
        return reservations.Select(MapToDto);
    }

    public async Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAndDateRangeAsync(
        Guid businessId,
        DateTime startDate,
        DateTime endDate)
    {
        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId,
            startDate,
            endDate);
        return reservations.Select(MapToDto);
    }

    /// <summary>
    /// Resuelve SelectedAddOnsCsv a (ServiceId, Service) para crear ReservationAddOns
    /// y retorna la lista de nombres para el mensaje de éxito.
    /// </summary>
    private async Task<(List<(Guid Id, Service Entity)>, IReadOnlyList<string>)> ResolveAddOnsAsync(
        Guid businessId,
        string? selectedAddOnsCsv,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedAddOnsCsv))
            return ([], []);

        var names = selectedAddOnsCsv
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var addOns = new List<(Guid Id, Service Entity)>();
        var resolvedNames = new List<string>();

        foreach (var name in names)
        {
            var addOnService = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(
                businessId, name.Trim());
            if (addOnService != null)
            {
                addOns.Add((addOnService.ServiceId, addOnService));
                resolvedNames.Add(addOnService.ServiceName);
            }
            else
            {
                _logger.LogWarning("Add-on '{AddOnName}' no encontrado en catálogo, omitiendo", name);
            }
        }

        return (addOns, resolvedNames);
    }

    private static Dictionary<string, string> BuildMetadata(CreateReservationRequest request)
    {
        var metadata = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
            metadata["CustomerName"] = request.CustomerName;
        if (!string.IsNullOrWhiteSpace(request.Email))
            metadata["Email"] = request.Email;
        if (!string.IsNullOrWhiteSpace(request.Phone))
            metadata["Phone"] = request.Phone;

        foreach (var kvp in request.BusinessAttributes)
            metadata[kvp.Key] = kvp.Value;

        return metadata;
    }

    private async Task<bool> IsCalendarSyncEnabledAsync(Guid businessId, CancellationToken cancellationToken)
    {
        var integrations = await _integrationsProvider.GetAsync(businessId, cancellationToken);
        return integrations?.GoogleCalendar?.Enabled ?? false;
    }

    private async Task<string?> TrySyncReservationToCalendarAsync(
        Reservation reservation,
        string serviceName,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var titleParts = new List<string> { $"[{serviceName}] Reserva" };
            foreach (var kvp in metadata)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                    titleParts.Add(kvp.Value);
            }
            var title = string.Join(" - ", titleParts);

            var description = $@"Reserva confirmada

                                Servicio: {serviceName}
                                Fecha: {reservation.ReservationDateTime:dd/MM/yyyy}
                                Hora: {reservation.ReservationDateTime:hh\:mm}
                                Duración: {reservation.DurationMinutes} minutos";

            if (metadata.Count > 0)
            {
                description += "\n\nInformación adicional:\n";
                foreach (var kvp in metadata)
                    description += $"{kvp.Key}: {kvp.Value}\n";
            }

            var calendarEvent = new CalendarEvent
            {
                Title = title,
                Description = description,
                StartDateTime = reservation.ReservationDateTime,
                EndDateTime = reservation.EndDateTime,
                ExtendedProperties = new Dictionary<string, string>
                {
                    { "ReservationId", reservation.ReservationId.ToString() },
                    { "BusinessId", reservation.BusinessId.ToString() }
                }
            };

            var eventId = await _calendarService.CreateEventAsync(reservation.BusinessId, calendarEvent, cancellationToken);
            _logger.LogInformation(
                "Evento de calendario creado para la reserva {ReservationId} con EventId {EventId}",
                reservation.ReservationId,
                eventId);
            return eventId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "No se pudo sincronizar la reserva {ReservationId} con el calendario. La reserva fue creada correctamente.",
                reservation.ReservationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SuspendAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
        if (reservation == null)
        {
            _logger.LogWarning("Reservation {ReservationId} no encontrada para Suspend", reservationId);
            return false;
        }

        if (reservation.Status == ReservationStatus.OnHold)
        {
            _logger.LogDebug("Reservation {ReservationId} ya está en OnHold", reservationId);
            return true;
        }

        reservation.Status = ReservationStatus.OnHold;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reserva {ReservationId} puesta en OnHold", reservationId);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RescheduleAsync(Guid reservationId, DateOnly newDate, TimeOnly newTime, CancellationToken cancellationToken = default)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
        if (reservation == null)
        {
            _logger.LogWarning("Reservation {ReservationId} no encontrada para Reschedule", reservationId);
            return false;
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            _logger.LogWarning("Reservation {ReservationId} cancelada no puede re-agendarse", reservationId);
            return false;
        }

        if (reservation.Status == ReservationStatus.OnHold)
        {
            reservation.Status = ReservationStatus.Confirmed;
        }

        var service = reservation.Service ?? await _unitOfWork.Services.GetByIdAsync(reservation.ServiceId);
        if (service == null)
        {
            _logger.LogWarning("Servicio de reserva {ReservationId} no encontrado", reservationId);
            return false;
        }

        var duration = service.DurationMinutes > 0 ? service.DurationMinutes : 60;
        var reservationDateTime = newDate.ToDateTime(newTime);
        var endTime = reservationDateTime.AddMinutes(duration);

        var employee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
            reservation.BusinessId,
            reservation.ServiceId,
            reservationDateTime,
            endTime,
            cancellationToken);

        if (employee == null)
        {
            _logger.LogWarning("No hay empleado disponible para nuevo horario {Date} {Time}", newDate, newTime);
            return false;
        }

        reservation.ReservationDateTime = reservationDateTime;
        reservation.DurationMinutes = duration;
        reservation.EmployeeId = employee.EmployeeId;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (await IsCalendarSyncEnabledAsync(reservation.BusinessId, cancellationToken) && !string.IsNullOrEmpty(reservation.CalendarEventId))
        {
            try
            {
                var serviceName = service.ServiceName;
                var calendarEvent = new CalendarEvent
                {
                    Title = $"[{serviceName}] Reserva",
                    Description = $"Reserva {reservationDateTime:dd/MM/yyyy} {reservationDateTime:HH:mm}",
                    StartDateTime = reservationDateTime,
                    EndDateTime = reservationDateTime.AddMinutes(duration),
                    ExtendedProperties = new Dictionary<string, string>
                    {
                        { "ReservationId", reservation.ReservationId.ToString() },
                        { "BusinessId", reservation.BusinessId.ToString() }
                    }
                };
                await _calendarService.UpdateEventAsync(reservation.BusinessId, reservation.CalendarEventId, calendarEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo actualizar evento de calendario para reserva {ReservationId}", reservationId);
            }
        }

        _logger.LogInformation("Reserva {ReservationId} re-agendada a {Date} {Time}", reservationId, newDate, newTime);
        return true;
    }

    private static ReservationDto MapToDto(Reservation reservation)
    {
        if (reservation.Service == null)
        {
            throw new InvalidOperationException(
                $"Service navigation property debe estar cargado para Reservation {reservation.ReservationId}. " +
                "Use Include(r => r.Service) al obtener la reserva.");
        }

        if (reservation.Employee == null)
        {
            throw new InvalidOperationException(
                $"Employee navigation property debe estar cargado para Reservation {reservation.ReservationId}. " +
                "Use Include(r => r.Employee) al obtener la reserva.");
        }

        return new ReservationDto
        {
            ReservationId = reservation.ReservationId,
            BusinessId = reservation.BusinessId,
            ServiceId = reservation.ServiceId,
            EmployeeId = reservation.EmployeeId,
            ServiceName = reservation.Service.ServiceName,
            EmployeeName = reservation.Employee.Name,
            ReservationDateTime = reservation.ReservationDateTime,
            DurationMinutes = reservation.DurationMinutes,
            Status = reservation.Status,
            CalendarEventId = reservation.CalendarEventId,
            ConversationId = reservation.ConversationId,
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt
        };
    }
}
