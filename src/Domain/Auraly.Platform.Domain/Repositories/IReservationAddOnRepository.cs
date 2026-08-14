using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IReservationAddOnRepository
{
    Task AddAsync(ReservationAddOn addOn);
    Task<IReadOnlyList<ReservationAddOn>> GetByReservationIdAsync(Guid reservationId);
    Task DeleteAsync(ReservationAddOn addOn);
}
