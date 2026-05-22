namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Política de reserva y pago por negocio.
/// Fuente única en runtime: BusinessConfiguration Key=BookingPolicy vía <see cref="IBookingPolicyProvider"/>.
/// </summary>
public class BookingPolicyParams
{
    /// <summary>Si true, se exige anticipo antes de confirmar la reserva.</summary>
    public bool DepositRequired { get; set; }

    /// <summary>Porcentaje del total que se cobra como anticipo (0–100).</summary>
    public int DepositPercentage { get; set; } = 50;

    /// <summary>Código ISO de moneda para links de pago (ej. COP).</summary>
    public string Currency { get; set; } = "COP";

    public static readonly BookingPolicyParams Default = new();

    /// <summary>
    /// Calcula el anticipo en centavos a partir del total en centavos y la política del negocio.
    /// </summary>
    public long CalculateDepositCents(long totalCents)
    {
        if (!DepositRequired || DepositPercentage <= 0 || totalCents <= 0)
            return 0;

        return totalCents * DepositPercentage / 100;
    }
}
