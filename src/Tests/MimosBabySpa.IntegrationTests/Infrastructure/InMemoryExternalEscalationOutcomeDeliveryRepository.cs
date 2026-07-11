using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

internal sealed class InMemoryExternalEscalationOutcomeDeliveryRepository : IExternalEscalationOutcomeDeliveryRepository
{
    private readonly List<ExternalEscalationOutcomeDelivery> _items = [];

    public Task<ExternalEscalationOutcomeDelivery?> GetByIdAsync(Guid deliveryId, CancellationToken ct = default) =>
        Task.FromResult(_items.FirstOrDefault(x => x.ExternalEscalationOutcomeDeliveryId == deliveryId));

    public Task<ExternalEscalationOutcomeDelivery?> GetByAttemptAndOutcomeAsync(Guid attemptId, string outcomeKey, CancellationToken ct = default) =>
        Task.FromResult(_items.FirstOrDefault(x =>
            x.ExternalEscalationAttemptId == attemptId &&
            x.OutcomeKey.Equals(outcomeKey, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<ExternalEscalationOutcomeDelivery>> GetPendingAsync(DateTime utcNow, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExternalEscalationOutcomeDelivery>>(_items
            .Where(x => x.PublishedAt is null && x.NextAttemptAt <= utcNow)
            .OrderBy(x => x.NextAttemptAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToList());

    public Task<ExternalEscalationOutcomeDelivery> AddAsync(ExternalEscalationOutcomeDelivery delivery, CancellationToken ct = default)
    {
        _items.Add(delivery);
        return Task.FromResult(delivery);
    }

    public Task<ExternalEscalationOutcomeDelivery> UpdateAsync(ExternalEscalationOutcomeDelivery delivery, CancellationToken ct = default) =>
        Task.FromResult(delivery);
}
