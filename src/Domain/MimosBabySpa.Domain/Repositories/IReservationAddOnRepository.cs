using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IReservationAddOnRepository
{
    Task AddAsync(ReservationAddOn addOn);
}
