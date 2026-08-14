namespace Auraly.Platform.Application.Configuration;

/// <summary>
/// Representa un método de pago aceptado por el negocio.
/// Details: opcional. Si está presente, se muestra en el resumen de anticipo (ej: Nequi: 311-123-4567).
/// </summary>
public class PaymentMethod
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? Details { get; set; }
}
