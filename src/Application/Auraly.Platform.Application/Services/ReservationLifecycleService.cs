using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public sealed class ReservationLifecycleService : IReservationLifecycleService
{
    private readonly IUnitOfWork _unitOfWork;

    public ReservationLifecycleService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Reservation?> GetActiveAsync(Guid conversationId, CancellationToken ct = default) =>
        _unitOfWork.Reservations.GetActiveByConversationIdAsync(conversationId, ct);

    public async Task<CustomerReservationSession> ResolveForSessionAsync(
        Guid conversationId,
        Guid businessId,
        string channelPhone,
        DateOnly businessToday,
        CancellationToken ct = default)
    {
        var reservations = new List<Reservation>();

        var byConversation = await _unitOfWork.Reservations.GetManageableByConversationIdAsync(
            conversationId,
            businessToday,
            ct);
        reservations.AddRange(byConversation);

        if (!string.IsNullOrWhiteSpace(channelPhone))
        {
            var byPhone = await _unitOfWork.Reservations.GetManageableByCustomerPhoneAsync(
                businessId,
                channelPhone.Trim(),
                businessToday,
                ct);
            reservations.AddRange(byPhone);
        }

        var manageable = reservations
            .Where(r => ReservationTemporalFormatter.IsManageableOnBusinessDay(r, businessToday))
            .GroupBy(r => r.ReservationId)
            .Select(g => g.First())
            .OrderBy(r => r.ReservationDateTime ?? DateTime.MaxValue)
            .ThenByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .ToList();

        return manageable.Count == 0
            ? CustomerReservationSession.None
            : CustomerReservationSession.From(manageable);
    }
}
