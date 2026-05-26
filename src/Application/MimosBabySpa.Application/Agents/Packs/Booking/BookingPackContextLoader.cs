using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Packs.Booking;

public sealed class BookingPackContextLoader : IPackContextLoader
{
    private readonly IBookingPolicyProvider _bookingPolicy;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IReservationLifecycleService _reservationLifecycle;

    public BookingPackContextLoader(
        IBookingPolicyProvider bookingPolicy,
        IPaymentLifecycleService paymentLifecycle,
        IReservationLifecycleService reservationLifecycle)
    {
        _bookingPolicy = bookingPolicy;
        _paymentLifecycle = paymentLifecycle;
        _reservationLifecycle = reservationLifecycle;
    }

    public string PackId => BookingPackIds.Booking;

    public async Task LoadAsync(AgentToolContext session, CancellationToken cancellationToken = default)
    {
        var bookingPolicy = await _bookingPolicy.GetAsync(session.BusinessId, cancellationToken);
        var activeReservation = await _reservationLifecycle.GetActiveAsync(session.ConversationId, cancellationToken);
        var activePayment = await _paymentLifecycle.GetActiveByConversationAsync(session.ConversationId, cancellationToken);

        session.SetPackContext(new BookingPackContext
        {
            BookingPolicy = bookingPolicy,
            ActiveReservation = activeReservation,
            ActivePayment = activePayment
        });
    }
}
