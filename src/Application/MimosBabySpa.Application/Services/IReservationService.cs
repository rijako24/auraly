using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IReservationService
{
    Task<CreateReservationResponse> CreateReservationAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea una reserva confirmada desde un snapshot inmutable (pago Wompi o reagendar pago huérfano).
    /// </summary>
    Task<CreateReservationResponse> CreateFromIntentSnapshotAsync(
        Guid businessId,
        Guid conversationId,
        ReservationIntentSnapshot snapshot,
        DateTime reservationDateTime,
        CancellationToken cancellationToken = default);
    Task<ReservationDto?> GetReservationByIdAsync(Guid reservationId);
    Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAsync(Guid businessId);
    Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAndDateRangeAsync(
        Guid businessId,
        DateTime startDate,
        DateTime endDate);

    /// <summary>
    /// Pone la reserva en espera ("no puede asistir", "avisa después"). Excluida de disponibilidad.
    /// </summary>
    Task<bool> SuspendAsync(Guid reservationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia fecha y hora de una reserva existente. Verificar disponibilidad antes de llamar.
    /// </summary>
    Task<bool> RescheduleAsync(Guid reservationId, DateOnly newDate, TimeOnly newTime, CancellationToken cancellationToken = default);
}
