using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IReservationLifecycleService
{
    Task<Reservation?> GetActiveAsync(Guid conversationId, CancellationToken ct = default);

    Task<CustomerReservationSession> ResolveForSessionAsync(
        Guid conversationId,
        Guid businessId,
        string channelPhone,
        DateOnly businessToday,
        CancellationToken ct = default);
}
