using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class ReservationLifecycleService : IReservationLifecycleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReservationLifecycleService> _logger;

    public ReservationLifecycleService(IUnitOfWork unitOfWork, ILogger<ReservationLifecycleService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task<Reservation?> GetActiveAsync(Guid conversationId, CancellationToken ct = default) =>
        _unitOfWork.Reservations.GetActiveByConversationIdAsync(conversationId, ct);

    public async Task<Reservation> GetOrCreateActiveAsync(Guid conversationId, Guid businessId, CancellationToken ct = default)
    {
        var existing = await GetActiveAsync(conversationId, ct);
        if (existing is not null)
            return existing;

        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = businessId,
            ConversationId = conversationId,
            Status = ReservationStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Reservations.CreateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Draft reservation {ReservationId} created for conversation {ConversationId}",
            reservation.ReservationId, conversationId);
        return reservation;
    }

    public async Task<Reservation> ApplyServiceByNameAsync(
        Reservation reservation, Guid businessId, string serviceName, CancellationToken ct = default)
    {
        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, serviceName)
            ?? throw new InvalidOperationException($"Service '{serviceName}' not found.");

        var changed = reservation.ServiceId != service.ServiceId;
        reservation.ServiceId = service.ServiceId;
        reservation.DurationMinutes = service.DurationMinutes > 0 ? service.DurationMinutes : 60;

        if (changed)
        {
            reservation.AvailableSlotsCsv = null;
            reservation.EmployeeId = null;
            reservation.CustomerConfirmed = false;
            if (reservation.Status is ReservationStatus.AvailabilityVerified or ReservationStatus.PendingPayment)
                reservation.Status = ReservationStatus.Draft;
        }

        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> ApplyDateTimeAsync(
        Reservation reservation, DateOnly date, TimeOnly time, CancellationToken ct = default)
    {
        var dateTime = date.ToDateTime(time);
        var changed = reservation.ReservationDateTime != dateTime;

        reservation.ReservationDateTime = dateTime;
        if (changed)
        {
            reservation.AvailableSlotsCsv = null;
            reservation.EmployeeId = null;
            reservation.CustomerConfirmed = false;
            if (reservation.Status is ReservationStatus.AvailabilityVerified or ReservationStatus.PendingPayment)
                reservation.Status = ReservationStatus.Draft;
        }

        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> SyncAddOnsFromCsvAsync(
        Reservation reservation, Guid businessId, string? addOnsCsv, CancellationToken ct = default)
    {
        var existingAddOns = await _unitOfWork.ReservationAddOns.GetByReservationIdAsync(reservation.ReservationId);
        foreach (var addOn in existingAddOns)
            await _unitOfWork.ReservationAddOns.DeleteAsync(addOn);

        if (!string.IsNullOrWhiteSpace(addOnsCsv))
        {
            var names = addOnsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var name in names)
            {
                if (string.Equals(name, "ninguno", StringComparison.OrdinalIgnoreCase))
                    continue;

                var addOnService = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, name);
                if (addOnService is null)
                {
                    _logger.LogWarning("Add-on '{AddOn}' not found, skipping", name);
                    continue;
                }

                await _unitOfWork.ReservationAddOns.AddAsync(new ReservationAddOn
                {
                    ReservationAddOnId = Guid.NewGuid(),
                    ReservationId = reservation.ReservationId,
                    AddOnServiceId = addOnService.ServiceId,
                    PriceSnapshot = addOnService.Price
                });
            }
        }

        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> ApplyCustomerSnapshotsAsync(
        Reservation reservation, string? name, string? email, string? phone, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(name)) reservation.CustomerNameSnapshot = name.Trim();
        if (!string.IsNullOrWhiteSpace(email)) reservation.CustomerEmailSnapshot = email.Trim();
        if (!string.IsNullOrWhiteSpace(phone)) reservation.CustomerPhoneSnapshot = phone.Trim();
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> MarkAvailabilityVerifiedAsync(
        Reservation reservation, Guid employeeId, string? slotsCsv, CancellationToken ct = default)
    {
        reservation.EmployeeId = employeeId;
        reservation.AvailableSlotsCsv = slotsCsv;
        reservation.Status = ReservationStatus.AvailabilityVerified;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> MarkCustomerConfirmedAsync(Reservation reservation, CancellationToken ct = default)
    {
        reservation.CustomerConfirmed = true;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> MarkPendingPaymentAsync(Reservation reservation, CancellationToken ct = default)
    {
        reservation.Status = ReservationStatus.PendingPayment;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> MarkConfirmedAsync(Reservation reservation, CancellationToken ct = default)
    {
        reservation.Status = ReservationStatus.Confirmed;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task CancelActiveAsync(Guid conversationId, CancellationToken ct = default)
    {
        var active = await GetActiveAsync(conversationId, ct);
        if (active is null) return;

        active.Status = ReservationStatus.Cancelled;
        active.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(active);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
