using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Application.Services;

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
