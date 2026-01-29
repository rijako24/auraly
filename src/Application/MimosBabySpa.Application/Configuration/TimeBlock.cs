namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Representa un bloque horario (horario de apertura y cierre)
/// </summary>
public class TimeBlock
{
    public string Open { get; set; } = string.Empty;  // Ej: "09:00"
    public string Close { get; set; } = string.Empty; // Ej: "18:00"
    
    /// <summary>
    /// Parsea el horario de apertura a TimeSpan para validaciones
    /// </summary>
    public TimeSpan OpenTime => TimeSpan.TryParse(Open, out var time) ? time : TimeSpan.Zero;
    
    /// <summary>
    /// Parsea el horario de cierre a TimeSpan para validaciones
    /// </summary>
    public TimeSpan CloseTime => TimeSpan.TryParse(Close, out var time) ? time : TimeSpan.Zero;
    
    /// <summary>
    /// Valida que el bloque horario sea válido
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Open) || string.IsNullOrWhiteSpace(Close))
            return false;
            
        if (!TimeSpan.TryParse(Open, out var open) || !TimeSpan.TryParse(Close, out var close))
            return false;
            
        // Close debe ser después de Open
        return close > open;
    }
}
