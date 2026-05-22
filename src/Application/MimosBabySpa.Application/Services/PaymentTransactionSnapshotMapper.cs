using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public static class PaymentTransactionSnapshotMapper
{
    public static ReservationIntentSnapshot? ToIntentSnapshot(PaymentTransaction payment, string? serviceName = null)
    {
        if (payment.Snapshot_ServiceId is null || !payment.Snapshot_ReservationDateTime.HasValue)
            return null;

        var addOnIds = ParseAddOnIds(payment.Snapshot_AddOnIds);
        return new ReservationIntentSnapshot(
            payment.Snapshot_ServiceId.Value,
            serviceName ?? string.Empty,
            payment.Snapshot_ReservationDateTime.Value,
            payment.Snapshot_DurationMinutes ?? 60,
            payment.Snapshot_PreferredEmployeeId,
            payment.Snapshot_CustomerName,
            payment.Snapshot_CustomerEmail,
            payment.Snapshot_CustomerPhone,
            addOnIds,
            payment.Snapshot_CustomAttributesJson);
    }

    public static IReadOnlyList<Guid> ParseAddOnIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
    }

    public static Reservation ToNotificationReservation(PaymentTransaction payment, string? serviceName)
    {
        return new Reservation
        {
            ReservationId = payment.ReservationId ?? Guid.Empty,
            BusinessId = payment.BusinessId,
            ServiceId = payment.Snapshot_ServiceId,
            ReservationDateTime = payment.Snapshot_ReservationDateTime,
            DurationMinutes = payment.Snapshot_DurationMinutes,
            CustomerNameSnapshot = payment.Snapshot_CustomerName,
            CustomerEmailSnapshot = payment.Snapshot_CustomerEmail,
            CustomerPhoneSnapshot = payment.Snapshot_CustomerPhone,
            CustomAttributesJson = payment.Snapshot_CustomAttributesJson,
            Service = payment.Snapshot_ServiceId.HasValue && !string.IsNullOrWhiteSpace(serviceName)
                ? new Service { ServiceId = payment.Snapshot_ServiceId.Value, ServiceName = serviceName }
                : null
        };
    }
}
