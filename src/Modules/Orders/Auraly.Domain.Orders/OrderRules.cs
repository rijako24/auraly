namespace Auraly.Domain.Orders;

public static class OrderRules
{
    public static string CanonicalStatus(
        int storedStatus,
        bool hasInvoice)
    {
        if (hasInvoice)
            return "Invoiced";

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

