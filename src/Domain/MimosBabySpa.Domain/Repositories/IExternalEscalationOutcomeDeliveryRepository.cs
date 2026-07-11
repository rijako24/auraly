using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IExternalEscalationOutcomeDeliveryRepository
{
    Task<ExternalEscalationOutcomeDelivery?> GetByIdAsync(Guid deliveryId, CancellationToken ct = default);
    Task<ExternalEscalationOutcomeDelivery?> GetByAttemptAndOutcomeAsync(Guid attemptId, string outcomeKey, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEscalationOutcomeDelivery>> GetPendingAsync(DateTime utcNow, int limit, CancellationToken ct = default);
    Task<ExternalEscalationOutcomeDelivery> AddAsync(ExternalEscalationOutcomeDelivery delivery, CancellationToken ct = default);
    Task<ExternalEscalationOutcomeDelivery> UpdateAsync(ExternalEscalationOutcomeDelivery delivery, CancellationToken ct = default);
}
