using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IReservationLifecycleService
{
    Task<Reservation?> GetActiveAsync(Guid conversationId, CancellationToken ct = default);
    Task<Reservation> GetOrCreateActiveAsync(Guid conversationId, Guid businessId, CancellationToken ct = default);
    Task<Reservation> ApplyServiceByNameAsync(Reservation reservation, Guid businessId, string serviceName, CancellationToken ct = default);
    Task<Reservation> ApplyDateTimeAsync(Reservation reservation, DateOnly date, TimeOnly time, CancellationToken ct = default);
    Task<Reservation> SyncAddOnsFromCsvAsync(Reservation reservation, Guid businessId, string? addOnsCsv, CancellationToken ct = default);
    Task<Reservation> ApplyCustomerSnapshotsAsync(Reservation reservation, string? name, string? email, string? phone, CancellationToken ct = default);
    Task<Reservation> MarkAvailabilityVerifiedAsync(Reservation reservation, Guid employeeId, string? slotsCsv, CancellationToken ct = default);
    Task<Reservation> MarkCustomerConfirmedAsync(Reservation reservation, CancellationToken ct = default);
    Task<Reservation> MarkPendingPaymentAsync(Reservation reservation, CancellationToken ct = default);
    Task<Reservation> MarkConfirmedAsync(Reservation reservation, CancellationToken ct = default);
    Task CancelActiveAsync(Guid conversationId, CancellationToken ct = default);
}
