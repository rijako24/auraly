using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IReservationIntegrationEventRepository
{
    Task<ReservationIntegrationEvent?> GetByReservationAndConnectionAsync(
        Guid reservationId,
        Guid integrationConnectionId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ReservationIntegrationEvent>> GetByReservationIdAsync(Guid reservationId, CancellationToken ct = default);
    Task<ReservationIntegrationEvent> AddAsync(ReservationIntegrationEvent integrationEvent, CancellationToken ct = default);
    Task<ReservationIntegrationEvent> UpdateAsync(ReservationIntegrationEvent integrationEvent, CancellationToken ct = default);
}
