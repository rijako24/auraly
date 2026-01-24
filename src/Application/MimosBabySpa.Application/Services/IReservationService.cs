using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IReservationService
{
    Task<ReservationDto> CreateReservationAsync(Reservation reservation, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
    Task<ReservationDto?> GetReservationByIdAsync(Guid reservationId);
    Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAsync(Guid businessId);
    Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAndDateRangeAsync(
        Guid businessId, 
        DateTime startDate, 
        DateTime endDate);
}
