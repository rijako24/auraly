using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IReservationMetadataRepository
{
    Task<IEnumerable<ReservationMetadata>> GetByReservationIdAsync(Guid reservationId);
    Task<ReservationMetadata> CreateAsync(ReservationMetadata metadata);
    Task CreateBatchAsync(IEnumerable<ReservationMetadata> metadata);
    Task DeleteByReservationIdAsync(Guid reservationId);
}
