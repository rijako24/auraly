using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

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
        var byConversation = await GetActiveAsync(conversationId, ct);
        if (byConversation is not null
            && ReservationTemporalFormatter.IsManageableOnBusinessDay(byConversation, businessToday))
        {
            return CustomerReservationSession.From([byConversation]);
        }

        if (string.IsNullOrWhiteSpace(channelPhone))
            return CustomerReservationSession.None;

        var byPhone = await _unitOfWork.Reservations.GetManageableByCustomerPhoneAsync(
            businessId, channelPhone.Trim(), businessToday, ct);

        return byPhone.Count == 0
            ? CustomerReservationSession.None
            : CustomerReservationSession.From(byPhone);
    }
}
