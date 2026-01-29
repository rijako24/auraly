using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

public class ExtractionValidator : IExtractionValidator
{
    private readonly ILogger<ExtractionValidator> _logger;

    public ExtractionValidator(ILogger<ExtractionValidator> logger)
    {
        _logger = logger;
    }

    public Task<ValidationResult> ValidateExtractionAsync(
        StructuredExtractionResponse extraction,
        string originalMessage,
        ConversationState currentState)
    {
        var result = new ValidationResult
        {
            IsValid = true,
            Confidence = 1.0,
            Issues = new List<string>()
        };

        // Validar respuesta conversacional no vacía
        if (string.IsNullOrWhiteSpace(extraction.ConversationalResponse))
        {
            result.Issues.Add("ConversationalResponse está vacía");
            result.Confidence *= 0.5;
        }

        // Validar confidence scores
        foreach (var field in extraction.ExtractedFields)
        {
            if (field.Confidence < 0.0 || field.Confidence > 1.0)
            {
                result.Issues.Add($"Confidence score inválido para {field.FieldName}: {field.Confidence}");
                result.IsValid = false;
            }

            if (field.Confidence < 0.5)
            {
                result.Issues.Add($"Confidence muy bajo para {field.FieldName}: {field.Confidence:F2}");
                result.Confidence *= 0.8;
            }
        }

        // Validar valores estructurados (no frases largas)
        foreach (var field in extraction.ExtractedFields)
        {
            if (field.Value.Split(' ').Length > 5)
            {
                result.Issues.Add($"Valor parece ser frase en vez de dato estructurado: {field.FieldName} = '{field.Value}'");
                result.Confidence *= 0.7;
            }
        }

        // Validar fechas
        var dateFields = extraction.ExtractedFields.Where(f => f.FieldType == FieldType.Date);
        foreach (var field in dateFields)
        {
            if (!DateOnly.TryParse(field.Value, out _))
            {
                result.Issues.Add($"Fecha inválida en {field.FieldName}: {field.Value}");
                result.IsValid = false;
            }
        }

        // Validar horas
        var timeFields = extraction.ExtractedFields.Where(f => f.FieldType == FieldType.Time);
        foreach (var field in timeFields)
        {
            if (!TimeOnly.TryParse(field.Value, out _))
            {
                result.Issues.Add($"Hora inválida en {field.FieldName}: {field.Value}");
                result.IsValid = false;
            }
        }

        // Calcular confianza final
        if (extraction.ExtractedFields.Any())
        {
            result.Confidence = Math.Max(0.0, result.Confidence * extraction.Metadata.AverageConfidence);
        }

        if (result.Issues.Any())
        {
            _logger.LogWarning(
                "Validación encontró {IssueCount} problema(s): {Issues}",
                result.Issues.Count,
                string.Join("; ", result.Issues));
        }

        return Task.FromResult(result);
    }
}
