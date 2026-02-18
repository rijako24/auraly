using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ReservationService : IReservationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICalendarService _calendarService;
    private readonly IBusinessConfigurationService _businessConfigService;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(
        IUnitOfWork unitOfWork,
        ICalendarService calendarService,
        IBusinessConfigurationService businessConfigService,
        ILogger<ReservationService> logger)
    {
        _unitOfWork = unitOfWork;
        _calendarService = calendarService;
        _businessConfigService = businessConfigService;
        _logger = logger;
    }

    public async Task<ReservationDto> CreateReservationAsync(
        Reservation reservation, 
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validar que el negocio existe
            var business = await _unitOfWork.Businesses.GetByIdAsync(reservation.BusinessId);
            if (business == null)
            {
                throw new InvalidOperationException($"El negocio con ID {reservation.BusinessId} no existe.");
            }

            // Cargar Service si no está cargado para obtener el nombre
            if (reservation.Service == null)
            {
                var service = await _unitOfWork.Services.GetByIdAsync(reservation.ServiceId);
                if (service == null)
                {
                    throw new InvalidOperationException($"El servicio con ID {reservation.ServiceId} no existe.");
                }
                reservation.Service = service;
            }

            var serviceName = reservation.Service.ServiceName;
            
            // Asegurar que ReservationId esté asignado
            if (reservation.ReservationId == Guid.Empty)
            {
                reservation.ReservationId = Guid.NewGuid();
            }

            // Asegurar CreatedAt
            if (reservation.CreatedAt == default)
            {
                reservation.CreatedAt = DateTime.UtcNow;
            }

            // Asegurar estado inicial
            if (reservation.Status == default)
            {
                reservation.Status = ReservationStatus.Pending;
            }

            // PASO 1: Crear evento en el calendario PRIMERO (antes de guardar en BD)
            string? eventId = null;
            try
            {
                // Construir título del evento con metadata si existe (genérico)
                var titleParts = new List<string> { $"[{serviceName}] Reserva" };
                if (metadata != null && metadata.Any())
                {
                    // Agregar todos los valores de metadata al título (genérico, sin campos hardcodeados)
                    foreach (var kvp in metadata)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            titleParts.Add(kvp.Value);
                        }
                    }
                }
                var title = string.Join(" - ", titleParts);

                // Template genérico para eventos de calendario
                var reservationTemplate = $@"Reserva confirmada

                                        Servicio: {serviceName}
                                        Fecha: {reservation.ReservationDateTime:dd/MM/yyyy}
                                        Hora: {reservation.ReservationDateTime:hh\:mm}
                                        Duración: {reservation.DurationMinutes} minutos";

                // Agregar metadata al description si existe
                if (metadata != null && metadata.Any())
                {
                    reservationTemplate += "\n\nInformación adicional:\n";
                    foreach (var kvp in metadata)
                    {
                        reservationTemplate += $"{kvp.Key}: {kvp.Value}\n";
                    }
                }

                var calendarEvent = new CalendarEvent
                {
                    Title = title,
                    Description = reservationTemplate,
                    StartDateTime = reservation.ReservationDateTime,
                    EndDateTime = reservation.EndDateTime,
                    ExtendedProperties = new Dictionary<string, string>
                    {
                        { "ReservationId", reservation.ReservationId.ToString() },
                        { "BusinessId", reservation.BusinessId.ToString() }
                    }
                };

                eventId = await _calendarService.CreateEventAsync(calendarEvent, cancellationToken);
                
                _logger.LogInformation(
                    "Evento de calendario creado exitosamente para la reserva {ReservationId} con EventId {EventId}",
                    reservation.ReservationId,
                    eventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error al crear evento en calendario para la reserva {ReservationId}",
                    reservation.ReservationId);
                throw; // Si falla el calendario, no crear la reserva
            }

            // PASO 2: Persistir reserva en base de datos después del calendario
            reservation.CalendarEventId = eventId;
            reservation.Status = ReservationStatus.Confirmed;
            reservation.UpdatedAt = DateTime.UtcNow;

            var createdReservation = await _unitOfWork.Reservations.CreateAsync(reservation);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Asegurar que Service esté cargado para logging y operaciones posteriores
            if (createdReservation.Service == null)
            {
                var loadedService = await _unitOfWork.Services.GetByIdAsync(createdReservation.ServiceId);
                if (loadedService == null)
                {
                    throw new InvalidOperationException($"El servicio con ID {createdReservation.ServiceId} no existe.");
                }
                createdReservation.Service = loadedService;
            }

            _logger.LogInformation(
                "Reserva creada exitosamente: {ReservationId} para servicio {ServiceName} el {DateTime}",
                createdReservation.ReservationId,
                createdReservation.Service?.ServiceName ?? "N/A",
                createdReservation.ReservationDateTime);

            return MapToDto(createdReservation);
        }
        catch (Exception ex)
        {
            var serviceName = reservation.Service?.ServiceName ?? reservation.ServiceId.ToString();
            _logger.LogError(ex, "Error al crear reserva para servicio {ServiceName}", serviceName);
            throw;
        }
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
