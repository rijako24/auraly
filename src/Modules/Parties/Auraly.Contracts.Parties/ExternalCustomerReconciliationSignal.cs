using System.Text.Json;

namespace Auraly.Contracts.Parties;

public sealed record ExternalCustomerReconciliationSignal(
    Guid MessageId,
    Guid ExternalCommerceCustomerId,
    Guid BusinessId,
    DateTimeOffset OccurredAt);

public static class ExternalCustomerReconciliationSignalCodec
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);

    public static string Serialize(ExternalCustomerReconciliationSignal signal)
    {
        Validate(signal);
        return JsonSerializer.Serialize(signal, Options);
    }

    public static ExternalCustomerReconciliationSignal Deserialize(string value)
    {
        var signal = JsonSerializer.Deserialize<ExternalCustomerReconciliationSignal>(
            value,
            Options) ?? throw new InvalidOperationException(
                "The external-customer reconciliation signal is invalid.");
        Validate(signal);
        return signal;
    }

    public static void Validate(ExternalCustomerReconciliationSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.MessageId == Guid.Empty ||
            signal.ExternalCommerceCustomerId == Guid.Empty ||
            signal.BusinessId == Guid.Empty ||
            signal.OccurredAt == default)
            throw new InvalidOperationException(
                "The external-customer reconciliation signal has invalid identifiers or occurrence time.");
    }
}
