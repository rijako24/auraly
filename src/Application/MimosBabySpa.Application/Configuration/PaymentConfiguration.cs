namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Configuración de pago por anticipo por negocio (multitenant).
/// </summary>
public class PaymentConfiguration
{
    public bool RequiresAnticipo { get; set; }
    public decimal AnticipoPorcentaje { get; set; } = 0.50m;
    public string Provider { get; set; } = "Wompi";
    public int LinkExpirationMinutes { get; set; } = 60;
    public string Currency { get; set; } = "COP";
}
