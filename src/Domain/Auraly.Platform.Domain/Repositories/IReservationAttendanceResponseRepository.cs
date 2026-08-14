using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IReservationAttendanceResponseRepository
{
    Task<ReservationAttendanceResponse?> GetLatestByReservationAsync(
        Guid businessId,
        Guid reservationId,
        CancellationToken ct = default);

    Task<ReservationAttendanceResponse> AddAsync(
        ReservationAttendanceResponse response,
        CancellationToken ct = default);
}
