using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Agents.Packs.Booking;

public static class BookingPackIds
{
    public const string Booking = "booking";
}

public interface IBookingPackContext : IPackContext
{
    BookingPolicyParams? BookingPolicy { get; }
    Reservation? ActiveReservation { get; }
    PaymentTransaction? ActivePayment { get; }
}

public sealed class BookingPackContext : IBookingPackContext
{
    public string PackId => BookingPackIds.Booking;
    public BookingPolicyParams? BookingPolicy { get; init; }
    public Reservation? ActiveReservation { get; init; }
    public PaymentTransaction? ActivePayment { get; init; }

    internal static void Replace(
        AgentToolContext ctx,
        BookingPolicyParams? bookingPolicy = null,
        Reservation? activeReservation = null,
        PaymentTransaction? activePayment = null)
    {
        var current = ctx.GetPackContext<IBookingPackContext>();
        ctx.SetPackContext(new BookingPackContext
        {
            BookingPolicy = bookingPolicy ?? current?.BookingPolicy,
            ActiveReservation = activeReservation ?? current?.ActiveReservation,
            ActivePayment = activePayment ?? current?.ActivePayment
        });
    }
}
