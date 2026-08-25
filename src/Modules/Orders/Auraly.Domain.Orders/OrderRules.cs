namespace Auraly.Domain.Orders;

public static class OrderRules
{
    public static string CanonicalStatus(
        int storedStatus,
        bool hasInvoice,
        string? processingStatus = null,
        string? processingJobStatus = null)
    {
        if (hasInvoice)
            return string.Equals(processingStatus, "Completed", StringComparison.Ordinal)
                ? "Invoiced"
                : string.Equals(processingJobStatus, "DeadLettered", StringComparison.Ordinal)
                    ? "EmissionFailed"
                    : "ProcessingEmission";

        return storedStatus switch
        {
            2 or 4 => "Available",
            6 => "Cancelled",
            7 => "AwaitingPayment",
            91 => "Expired",
            _ => "Pending"
        };
    }

    public static bool CanInvoice(
        int storedStatus,
        bool customerConfirmed,
        bool hasInvoice) =>
        customerConfirmed &&
        !hasInvoice &&
        storedStatus is 2 or 4;

    public static int LeaseMinutes(int requested) =>
        Math.Clamp(requested, 2, 30);
}
