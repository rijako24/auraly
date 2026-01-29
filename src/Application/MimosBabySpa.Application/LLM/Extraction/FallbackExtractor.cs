using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

public class FallbackExtractor : IFallbackExtractor
{
    private readonly ILogger<FallbackExtractor> _logger;
    private readonly IBusinessConfigurationProvider _configProvider;

    public FallbackExtractor(
        ILogger<FallbackExtractor> logger,
        IBusinessConfigurationProvider configProvider)
    {
        _logger = logger;
        _configProvider = configProvider;
    }

    public async Task<StructuredExtractionResponse> ExtractAsync(
        string userMessage,
        ConversationState currentState,
        Guid businessId,
        StructuredExtractionResponse? llmAttempt,
        CancellationToken cancellationToken = default)
    {
        var response = new StructuredExtractionResponse
        {
            ExtractedFields = new List<ExtractedField>(),
            Ambiguities = llmAttempt?.Ambiguities ?? new List<AmbiguityDetection>(),
            Metadata = new ExtractionMetadata()
        };

        var messageLower = userMessage.ToLowerInvariant();

        // Cargar atributos del negocio para extracción genérica
        var attributes = await _configProvider.GetBusinessAttributesAsync(businessId, cancellationToken);

        // REGLA 1: Extraer atributos del negocio usando patrones de validación configurados
        foreach (var attribute in attributes)
        {
            if (!string.IsNullOrEmpty(attribute.Value.ValidationPattern))
            {
                var match = Regex.Match(userMessage, attribute.Value.ValidationPattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    response.ExtractedFields.Add(new ExtractedField
                    {
                        FieldName = $"Attribute:{attribute.Key}",
                        Value = value,
                        FieldType = MapAttributeTypeToFieldType(attribute.Value.Type),
                        Confidence = 0.8,
                        Reasoning = $"Extracción regex: patrón configurado para {attribute.Key}",
                        SourceText = match.Value
                    });
                }
            }
        }

        // REGLA 2: Detectar confirmaciones explícitas (genérico para cualquier negocio)
        var confirmationPatterns = new[] { @"\b(sí|si|confirmo|adelante|ok|vale|perfecto|de acuerdo|está bien)\b" };
        foreach (var pattern in confirmationPatterns)
        {
            if (Regex.IsMatch(messageLower, pattern))
            {
                response.FlowAnalysis.UserConfirmedBooking = true;
                response.FlowAnalysis.ConfirmationConfidence = 0.8;
                response.FlowAnalysis.ConfirmationIndicators.Add(pattern);
                break;
            }
        }

        // REGLA 3: Campos core genéricos - Fechas temporales
        ExtractTemporalDates(messageLower, response);

        // REGLA 4: Campos core genéricos - Horas
        ExtractTimes(userMessage, response);

        // Usar respuesta del LLM si existe, sino generar una genérica
        response.ConversationalResponse = !string.IsNullOrEmpty(llmAttempt?.ConversationalResponse)
            ? llmAttempt.ConversationalResponse
            : "Entiendo. ¿Podrías darme más detalles para ayudarte mejor?";

        response.Metadata.FieldsExtracted = response.ExtractedFields.Count;
        response.Metadata.AverageConfidence = response.ExtractedFields.Any()
            ? response.ExtractedFields.Average(f => f.Confidence)
            : 0.0;

        _logger.LogInformation(
            "Fallback extraction completada: {FieldCount} campos con confidence {Confidence:F2}",
            response.ExtractedFields.Count,
            response.Metadata.AverageConfidence);

        return response;
    }

    private static void ExtractTemporalDates(string messageLower, StructuredExtractionResponse response)
    {
        if (messageLower.Contains("mañana"))
        {
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName = "DesiredDate",
                Value = DateOnly.FromDateTime(DateTime.Now.AddDays(1)).ToString("yyyy-MM-dd"),
                FieldType = FieldType.Date,
                Confidence = 0.9,
                Reasoning = "Interpretación de 'mañana'",
                SourceText = "mañana"
            });
        }
        else if (messageLower.Contains("hoy"))
        {
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName = "DesiredDate",
                Value = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
                FieldType = FieldType.Date,
                Confidence = 0.9,
                Reasoning = "Interpretación de 'hoy'",
                SourceText = "hoy"
            });
        }
    }

    private static void ExtractTimes(string userMessage, StructuredExtractionResponse response)
    {
        // Patrón para horas PM (3pm, 3 de la tarde, etc.)
        var timeMatch = Regex.Match(userMessage, @"\b(\d{1,2})\s*(?:pm|de la tarde|de la noche)\b", RegexOptions.IgnoreCase);
        if (timeMatch.Success && int.TryParse(timeMatch.Groups[1].Value, out var hour))
        {
            var hour24 = hour == 12 ? 12 : hour + 12;
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName = "DesiredTime",
                Value = $"{hour24:D2}:00",
                FieldType = FieldType.Time,
                Confidence = 0.85,
                Reasoning = "Extracción regex: hora PM",
                SourceText = timeMatch.Value
            });
        }

        // Patrón para formato 24h (15:00, 15:30, etc.)
        var time24Match = Regex.Match(userMessage, @"\b(\d{1,2}):(\d{2})\b");
        if (time24Match.Success && 
            int.TryParse(time24Match.Groups[1].Value, out var h24) && 
            int.TryParse(time24Match.Groups[2].Value, out var m24) &&
            h24 >= 0 && h24 < 24 && m24 >= 0 && m24 < 60)
        {
            response.ExtractedFields.Add(new ExtractedField
            {
                FieldName = "DesiredTime",
                Value = $"{h24:D2}:{m24:D2}",
                FieldType = FieldType.Time,
                Confidence = 0.9,
                Reasoning = "Extracción regex: formato 24h",
                SourceText = time24Match.Value
            });
        }
    }

    private static FieldType MapAttributeTypeToFieldType(AttributeType attributeType)
    {
        return attributeType switch
        {
            AttributeType.Text => FieldType.Text,
            AttributeType.Number => FieldType.Number,
            AttributeType.Date => FieldType.Date,
            AttributeType.Time => FieldType.Time,
            AttributeType.Email => FieldType.Email,
            _ => FieldType.Text
        };
    }
}
