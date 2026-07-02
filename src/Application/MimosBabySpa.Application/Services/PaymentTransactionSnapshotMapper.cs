using System.Text.Json;
using System.Text.Json.Serialization;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public static class PaymentTransactionSnapshotMapper
{
    private static readonly HashSet<string> UniversalFactKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ConversationFactKeys.CustomerName,
        ConversationFactKeys.CustomerPhone,
        ConversationFactKeys.CustomerEmail,
        ConversationFactKeys.Service,
        ConversationFactKeys.DesiredDate,
        ConversationFactKeys.DesiredTime,
        ConversationFactKeys.AddOns
    };

    public static ReservationIntentSnapshot? ToIntentSnapshot(PaymentTransaction payment, string? serviceName = null)
    {
        var snapshot = ParseCheckoutSnapshot(payment.CheckoutSnapshotJson);
        if (snapshot is null || snapshot.ServiceId == Guid.Empty)
            return null;

        if (!AgentDateRules.TryParseDate(snapshot.ReservationDate, out var date))
            return null;
        if (!TimeOnly.TryParse(snapshot.ReservationTime, out var time))
            return null;

        return new ReservationIntentSnapshot(
            snapshot.ServiceId,
            serviceName ?? snapshot.ServiceName ?? string.Empty,
            date.ToDateTime(time),
            snapshot.DurationMinutes > 0 ? snapshot.DurationMinutes : 60,
            PreferredEmployeeId: null,
            snapshot.PayerName,
            snapshot.PayerEmail,
            snapshot.PaymentPhone,
            [],
            BuildCustomAttributesJson(snapshot.Facts));
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
        var snapshot = ToIntentSnapshot(payment, serviceName);
        return new Reservation
        {
            ReservationId = payment.ReservationId ?? Guid.Empty,
            BusinessId = payment.BusinessId,
            ServiceId = snapshot?.ServiceId,
            ReservationDateTime = snapshot?.ReservationDateTime,
            DurationMinutes = snapshot?.DurationMinutes,
            CustomerNameSnapshot = snapshot?.CustomerName,
            CustomerEmailSnapshot = snapshot?.CustomerEmail,
            CustomerPhoneSnapshot = snapshot?.CustomerPhone,
            CustomAttributesJson = snapshot?.CustomAttributesJson,
            Service = snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.ServiceName)
                ? new Service { ServiceId = snapshot.ServiceId, ServiceName = snapshot.ServiceName }
                : null
        };
    }

    private static string BuildCustomAttributesJson(IReadOnlyDictionary<string, string>? facts)
    {
        var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (facts is not null)
        {
            foreach (var (key, value) in facts)
            {
                if (UniversalFactKeys.Contains(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                custom[key] = value.Trim();
            }
        }

        return custom.Count == 0 ? "{}" : JsonSerializer.Serialize(custom);
    }

    private static CheckoutSnapshot? ParseCheckoutSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<CheckoutSnapshot>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class CheckoutSnapshot
    {
        [JsonPropertyName("service_id")]
        public Guid ServiceId { get; set; }

        [JsonPropertyName("service_name")]
        public string? ServiceName { get; set; }

        [JsonPropertyName("duration_minutes")]
        public int DurationMinutes { get; set; }

        [JsonPropertyName("payer_name")]
        public string? PayerName { get; set; }

        [JsonPropertyName("payment_phone")]
        public string? PaymentPhone { get; set; }

        [JsonPropertyName("payer_email")]
        public string? PayerEmail { get; set; }

        [JsonPropertyName("reservation_date")]
        public string? ReservationDate { get; set; }

        [JsonPropertyName("reservation_time")]
        public string? ReservationTime { get; set; }

        [JsonPropertyName("facts")]
        public Dictionary<string, string>? Facts { get; set; }
    }
}