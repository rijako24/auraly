namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Resultado de validación de atributo
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
}
