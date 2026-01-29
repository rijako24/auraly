namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Representa un método de pago aceptado por el negocio
/// </summary>
public class PaymentMethod
{
    public string Name { get; set; } = string.Empty; // Ej: "Efectivo", "Tarjeta"
    public string Icon { get; set; } = string.Empty; // Ej: "💵", "💳"
}
