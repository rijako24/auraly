using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public sealed class ReservationResolveResult
{
    public bool Success { get; init; }
    public Reservation? Reservation { get; init; }
    public string? ErrorJson { get; init; }

    public static ReservationResolveResult Ok(Reservation reservation) =>
        new() { Success = true, Reservation = reservation };

    public static ReservationResolveResult Fail(string errorJson) =>
        new() { Success = false, ErrorJson = errorJson };
}

public interface ICustomerReservationResolver
{
    /// <summary>
    /// Resuelve la reserva objetivo para tools post-booking. <paramref name="reservationIdFromArgs"/> es opcional.
    /// </summary>
    Task<ReservationResolveResult> ResolveAsync(
        AgentToolContext ctx,
        string? reservationIdFromArgs,
        CancellationToken ct = default);
}
