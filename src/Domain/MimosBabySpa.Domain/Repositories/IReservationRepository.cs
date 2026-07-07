using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(Guid reservationId);
    Task<IEnumerable<Reservation>> GetByBusinessIdAsync(Guid businessId);
    Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<IEnumerable<Reservation>> GetByBusinessIdAndDateRangeAsync(
        Guid businessId,
        DateTime startDate,
        DateTime endDate);

    /// <summary>
    /// Gets the most recent reservations for dashboard display.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetRecentByBusinessIdAsync(
        Guid businessId, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<Reservation>> GetUpcomingConfirmedByBusinessIdAsync(
        Guid businessId,
        DateTime fromLocal,
        DateTime toLocal,
        CancellationToken ct = default);

    Task<IReadOnlyList<Reservation>> GetLatestCompletedCustomerReservationsWithoutFutureAsync(
        Guid businessId,
        DateTime completedBeforeUtc,
        DateTime futureFromUtc,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Gets top services by reservation count and estimated revenue (Service.Price) for dashboard.
    /// </summary>
    Task<IReadOnlyList<(Guid ServiceId, string ServiceName, int TotalReservations, decimal Revenue)>> GetTopServicesByBusinessIdAsync(
        Guid businessId, int limit, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    Task<Reservation> CreateAsync(Reservation reservation);
    Task<Reservation> UpdateAsync(Reservation reservation);
    Task<Reservation?> GetActiveByConversationIdAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Reservas confirmadas o en espera creadas en la conversacion, con cita hoy o futura segun el dia del negocio.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetManageableByConversationIdAsync(
        Guid conversationId,
        DateOnly businessToday,
        CancellationToken ct = default);

    /// <summary>
    /// Reservas confirmadas o en espera del cliente (telefono), con cita hoy o futura segun el dia del negocio.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetManageableByCustomerPhoneAsync(
        Guid businessId,
        string customerPhone,
        DateOnly businessToday,
        CancellationToken ct = default);

    Task<bool> ExistsOverlappingReservationAsync(
        Guid businessId,
        DateTime reservationDate,
        TimeSpan reservationTime,
        int durationMinutes,
        Guid? excludeReservationId = null);
}
