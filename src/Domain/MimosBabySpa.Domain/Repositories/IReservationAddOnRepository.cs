using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IReservationAddOnRepository
{
    Task AddAsync(ReservationAddOn addOn);
    Task<IReadOnlyList<ReservationAddOn>> GetByReservationIdAsync(Guid reservationId);
    Task DeleteAsync(ReservationAddOn addOn);
}
