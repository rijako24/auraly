namespace Auraly.Domain.Receivables;

public sealed record CustomerCreditDecision(bool IsAllowed, decimal? AvailableCredit, string? RejectionReason);

/// <summary>One decision for new credit, shared by batch preview and final acceptance.</summary>
public static class CustomerCreditPolicy
{
    public static CustomerCreditDecision Evaluate(bool enabled, decimal? limit, decimal exposure,
        int overdueInvoices, int? maximumOverdueInvoices, decimal requestedCredit)
    {
        if (overdueInvoices < 0 || maximumOverdueInvoices < 0 || requestedCredit < 0)
            throw new ArgumentOutOfRangeException(nameof(requestedCredit), "Las condiciones de crédito no pueden ser negativas.");
        var available = CalculateAvailableCredit(limit, exposure);
        if (requestedCredit == 0) return new(true, available, null);
        if (!enabled) return new(false, null, "El cliente no tiene habilitada la venta a crédito.");
        if (maximumOverdueInvoices is { } maximum && overdueInvoices > maximum)
            return new(false, available,
                $"El cliente tiene {overdueInvoices} facturas vencidas; el máximo permitido es {maximum}.");
        return available is { } remaining && requestedCredit > remaining
            ? new(false, available, "La venta supera el cupo disponible del cliente.")
            : new(true, available, null);
    }

    public static decimal? CalculateAvailableCredit(decimal? limit, decimal exposure)
    {
        if (limit < 0 || exposure < 0)
            throw new ArgumentOutOfRangeException(nameof(exposure), "El cupo y su consumo no pueden ser negativos.");
        return limit is null ? null : Math.Max(0, limit.Value - exposure);
    }
}
