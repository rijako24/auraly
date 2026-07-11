using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ExternalEscalationOutcomeDeliveryRepository : IExternalEscalationOutcomeDeliveryRepository
{
    private readonly ApplicationDbContext _context;

    public ExternalEscalationOutcomeDeliveryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ExternalEscalationOutcomeDelivery?> GetByIdAsync(Guid deliveryId, CancellationToken ct = default) =>
        _context.ExternalEscalationOutcomeDeliveries.FirstOrDefaultAsync(x => x.ExternalEscalationOutcomeDeliveryId == deliveryId, ct);

    public Task<ExternalEscalationOutcomeDelivery?> GetByAttemptAndOutcomeAsync(Guid attemptId, string outcomeKey, CancellationToken ct = default) =>
        _context.ExternalEscalationOutcomeDeliveries.FirstOrDefaultAsync(x =>
            x.ExternalEscalationAttemptId == attemptId && x.OutcomeKey == outcomeKey, ct);

    public async Task<IReadOnlyList<ExternalEscalationOutcomeDelivery>> GetPendingAsync(DateTime utcNow, int limit, CancellationToken ct = default) =>
        await _context.ExternalEscalationOutcomeDeliveries
            .Where(x => x.PublishedAt == null && x.NextAttemptAt <= utcNow)
            .OrderBy(x => x.NextAttemptAt)
            .ThenBy(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

    public Task<ExternalEscalationOutcomeDelivery> AddAsync(ExternalEscalationOutcomeDelivery delivery, CancellationToken ct = default)
    {
        _context.ExternalEscalationOutcomeDeliveries.Add(delivery);
        return Task.FromResult(delivery);
    }

    public Task<ExternalEscalationOutcomeDelivery> UpdateAsync(ExternalEscalationOutcomeDelivery delivery, CancellationToken ct = default)
    {
        _context.ExternalEscalationOutcomeDeliveries.Update(delivery);
        return Task.FromResult(delivery);
    }
}
