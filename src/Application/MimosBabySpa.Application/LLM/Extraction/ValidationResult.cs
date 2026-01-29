namespace MimosBabySpa.Application.LLM.Extraction;

public class ValidationResult
{
    public bool IsValid { get; set; }
    public double Confidence { get; set; }
    public List<string> Issues { get; set; } = new();
}
