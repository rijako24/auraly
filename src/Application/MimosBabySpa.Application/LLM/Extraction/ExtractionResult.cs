namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Resultado de la extracción con validación
/// </summary>
public class ExtractionResult
{
    public bool Success { get; set; }
    public ExtractionMethod Method { get; set; }
    public StructuredExtractionResponse StructuredResponse { get; set; } = new();
    public ValidationResult ValidationResult { get; set; } = new();
}
