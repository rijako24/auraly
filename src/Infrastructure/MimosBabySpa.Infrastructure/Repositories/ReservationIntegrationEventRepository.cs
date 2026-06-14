using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ReservationIntegrationEventRepository : IReservationIntegrationEventRepository
{
    private readonly ApplicationDbContext _context;

    public ReservationIntegrationEventRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ReservationIntegrationEvent?> GetByReservationAndConnectionAsync(
        Guid reservationId,
        Guid integrationConnectionId,
        CancellationToken ct = default)
    {
        return await _context.ReservationIntegrationEvents
            .FirstOrDefaultAsync(e =>
                e.ReservationId == reservationId &&
                e.IntegrationConnectionId == integrationConnectionId,
                ct);
    }

    public async Task<IReadOnlyList<ReservationIntegrationEvent>> GetByReservationIdAsync(Guid reservationId, CancellationToken ct = default)
    {
        return await _context.ReservationIntegrationEvents
            .Where(e => e.ReservationId == reservationId)
            .ToListAsync(ct);
    }

    public Task<ReservationIntegrationEvent> AddAsync(ReservationIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        _context.ReservationIntegrationEvents.Add(integrationEvent);
        return Task.FromResult(integrationEvent);
    }

    public Task<ReservationIntegrationEvent> UpdateAsync(ReservationIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        _context.ReservationIntegrationEvents.Update(integrationEvent);
        return Task.FromResult(integrationEvent);
    }
}
