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
    private readonly INotesFormatterService _notesFormatterService;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(
        IUnitOfWork unitOfWork,
        ICalendarService calendarService,
        IBusinessConfigurationService businessConfigService,
        INotesFormatterService notesFormatterService,
        ILogger<ReservationService> logger)
    {
        _unitOfWork = unitOfWork;
        _calendarService = calendarService;
        _businessConfigService = businessConfigService;
        _notesFormatterService = notesFormatterService;
        _logger = logger;
    }

    public async Task<ReservationDto> CreateReservationAsync(
        Reservation reservation, 
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

            // Template genérico para eventos de calendario
            // Solo usa campos genéricos: service, date, time, durationMinutes
            // Toda la información adicional viene en Notes y se formatea de forma legible
            var notesInfo = _notesFormatterService.FormatNotes(reservation.Notes);
            
            var reservationTemplate = $@"Reserva confirmada

                                        Servicio: {reservation.ServiceName}
                                        Fecha: {reservation.ReservationDate:dd/MM/yyyy}
                                        Hora: {reservation.ReservationTime:hh\:mm}
                                        Duración: {reservation.DurationMinutes} minutos{notesInfo}";

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

            // Persistir en base de datos primero
            var createdReservation = await _unitOfWork.Reservations.CreateAsync(reservation);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Reserva creada exitosamente: {ReservationId} para servicio {ServiceName} el {Date} a las {Time}",
                createdReservation.ReservationId,
                createdReservation.ServiceName,
                createdReservation.ReservationDate,
                createdReservation.ReservationTime);

            // Intentar crear el evento en el calendario
            try
            {
                // Usar el template (ya tiene los valores reemplazados o es el template por defecto)
                var description = reservationTemplate;

                // Título genérico del evento - solo usa el servicio
                var title = $"[{reservation.ServiceName}] Reserva";

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

                var eventId = await _calendarService.CreateEventAsync(calendarEvent, cancellationToken);
                
                // Actualizar la reserva con el ID del evento
                createdReservation.CalendarEventId = eventId;
                createdReservation.Status = ReservationStatus.Confirmed;
                createdReservation.UpdatedAt = DateTime.UtcNow;
                
                await _unitOfWork.Reservations.UpdateAsync(createdReservation);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Evento de calendario creado exitosamente para la reserva {ReservationId} con EventId {EventId}",
                    createdReservation.ReservationId,
                    eventId);
            }
            catch (Exception ex)
            {
                // Si falla el calendario, la reserva queda con estado PendingCalendar
                _logger.LogError(ex,
                    "Error al crear evento en calendario para la reserva {ReservationId}. La reserva queda con estado PendingCalendar",
                    createdReservation.ReservationId);

                createdReservation.Status = ReservationStatus.PendingCalendar;
                createdReservation.UpdatedAt = DateTime.UtcNow;
                
                await _unitOfWork.Reservations.UpdateAsync(createdReservation);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return MapToDto(createdReservation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear reserva para servicio {ServiceName}", reservation.ServiceName);
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
        return new ReservationDto
        {
            ReservationId = reservation.ReservationId,
            BusinessId = reservation.BusinessId,
            CustomerName = reservation.CustomerName,
            PhoneNumber = reservation.PhoneNumber,
            ServiceName = reservation.ServiceName,
            ReservationDate = reservation.ReservationDate,
            ReservationTime = reservation.ReservationTime,
            DurationMinutes = reservation.DurationMinutes,
            Status = reservation.Status,
            CalendarEventId = reservation.CalendarEventId,
            Notes = reservation.Notes,
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt,
            ReservationDateTime = reservation.ReservationDateTime,
            EndDateTime = reservation.EndDateTime
        };
    }
}
