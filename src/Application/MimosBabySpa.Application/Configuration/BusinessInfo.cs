namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Información del negocio
/// </summary>
public class BusinessInfo
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    
    /// <summary>
    /// Horarios de operación por día de la semana.
    /// Cada día puede tener múltiples bloques horarios (ej: mañana y tarde).
    /// Clave: nombre del día en inglés lowercase (monday, tuesday, etc.)
    /// Valor: Lista de bloques horarios. Lista vacía = día cerrado.
    /// </summary>
    public Dictionary<string, List<TimeBlock>> Schedule { get; set; } = new();
    
    /// <summary>
    /// Métodos de pago aceptados por el negocio
    /// </summary>
    public List<PaymentMethod> PaymentMethods { get; set; } = new();
    
    public string? LogoUrl { get; set; }
}
