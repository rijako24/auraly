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

    /// <summary>Código ISO de moneda para links de pago (ej. COP). Sin default en runtime — configurar por negocio.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Minutos de expiración del link de pago.</summary>
    public int PaymentLinkExpirationMinutes { get; set; } = 60;

    /// <summary>Duración default de servicio cuando el catálogo no define duración.</summary>
    public int DefaultServiceDurationMinutes { get; set; } = 60;

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
