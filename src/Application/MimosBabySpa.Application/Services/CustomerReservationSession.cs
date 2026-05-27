using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Reservas del cliente que se pueden reagendar o suspender en este turno (0, 1 o varias).
/// </summary>
public sealed class CustomerReservationSession
{
    public IReadOnlyList<Reservation> ManageableReservations { get; init; } = [];

    public static CustomerReservationSession None { get; } = new();

    public static CustomerReservationSession From(IReadOnlyList<Reservation> reservations) =>
        new() { ManageableReservations = reservations };
}
