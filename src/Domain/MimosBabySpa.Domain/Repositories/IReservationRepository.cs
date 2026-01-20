using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(Guid reservationId);
    Task<IEnumerable<Reservation>> GetByBusinessIdAsync(Guid businessId);
    Task<IEnumerable<Reservation>> GetByBusinessIdAndDateRangeAsync(
        Guid businessId, 
        DateTime startDate, 
        DateTime endDate);
    Task<IEnumerable<Reservation>> GetByPhoneNumberAsync(string phoneNumber);
    Task<Reservation> CreateAsync(Reservation reservation);
    Task<Reservation> UpdateAsync(Reservation reservation);
    Task<bool> ExistsOverlappingReservationAsync(
        Guid businessId, 
        DateTime startDateTime, 
        DateTime endDateTime, 
        Guid? excludeReservationId = null);
}
