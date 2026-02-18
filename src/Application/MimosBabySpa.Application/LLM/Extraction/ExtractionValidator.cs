using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Valida la respuesta estructurada del LLM.
///
/// Diseño binario (valid/invalid) + confidence promediado:
/// - Invalida si hay errores de FORMATO (fecha no parseable, hora no parseable, valor demasiado largo).
/// - Invalida si confidence está fuera de rango [0,1].
/// - ConversationalResponse vacía NO invalida la extracción (la respuesta la genera FASE 5).
/// - Confidence = promedio de campos extraídos (no multiplicación cascada).
/// - Sin campos extraídos = válido con confidence 1.0 (puede ser mensaje conversacional sin datos).
/// </summary>
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
            IsValid    = true,
            Confidence = 1.0,
            Issues     = new List<string>()
        };

        foreach (var field in extraction.ExtractedFields)
        {
            // 1. Confidence en rango válido
            if (field.Confidence < 0.0 || field.Confidence > 1.0)
            {
                result.IsValid = false;
                result.Issues.Add($"Confidence fuera de rango [{field.FieldName}={field.Confidence:F2}]");
                continue;
            }

            // 2. Formato correcto según field_type inferido del nombre
            var inferredType = InferFieldType(field.FieldName, field.Value);
            if (inferredType == FieldType.Date && !DateOnly.TryParse(field.Value, out _))
            {
                result.IsValid = false;
                result.Issues.Add($"Fecha inválida [{field.FieldName}='{field.Value}']");
                continue;
            }
            if (inferredType == FieldType.Time && !TimeOnly.TryParse(field.Value, out _))
            {
                result.IsValid = false;
                result.Issues.Add($"Hora inválida [{field.FieldName}='{field.Value}']");
                continue;
            }

            // 3. Valor estructurado (no frase larga)
            if (field.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > ExtractionConstants.MaxValueWordCount)
            {
                result.IsValid = false;
                result.Issues.Add($"Valor parece frase, no dato estructurado [{field.FieldName}='{field.Value}']");
            }
        }

        // Confidence = promedio simple de campos (si hay). Sin campos → 1.0 (válido por defecto).
        result.Confidence = extraction.ExtractedFields.Any()
            ? extraction.ExtractedFields.Average(f => f.Confidence)
            : 1.0;

        if (result.Issues.Any())
            _logger.LogWarning("Validación: {Count} problema(s): {Issues}",
                result.Issues.Count, string.Join("; ", result.Issues));

        return Task.FromResult(result);
    }

    // ─────────────────────────────────────────────────────────────────
    // InferFieldType (duplicado del SmartExtractionService para desacoplar)
    // ─────────────────────────────────────────────────────────────────

    private static FieldType InferFieldType(string fieldName, string value)
    {
        var lower = fieldName.ToLowerInvariant();

        if (lower.Contains("date") || lower.Contains("fecha")) return FieldType.Date;
        if (lower.Contains("time") || lower.Contains("hora"))  return FieldType.Time;
        if (lower.Contains("email") || lower.Contains("correo")) return FieldType.Email;
        if (lower.Contains("phone") || lower.Contains("telefono") || lower.Contains("celular")) return FieldType.Phone;
        if (lower.Contains("service") || lower.Contains("servicio")) return FieldType.Service;

        // Infiere por valor
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$")) return FieldType.Date;
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{2}:\d{2}$"))        return FieldType.Time;

        return FieldType.Text;
    }
}
