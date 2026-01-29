using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Constants;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

public class SmartExtractionService : ISmartExtractionService
{
    private readonly ILLMAdapter _llmAdapter;
    private readonly JsonSchemaPromptBuilder _promptBuilder;
    private readonly IExtractionValidator _validator;
    private readonly IFallbackExtractor _fallbackExtractor;
    private readonly ILogger<SmartExtractionService> _logger;

    public SmartExtractionService(
        ILLMAdapter llmAdapter,
        JsonSchemaPromptBuilder promptBuilder,
        IExtractionValidator validator,
        IFallbackExtractor fallbackExtractor,
        ILogger<SmartExtractionService> logger)
    {
        _llmAdapter = llmAdapter;
        _promptBuilder = promptBuilder;
        _validator = validator;
        _fallbackExtractor = fallbackExtractor;
        _logger = logger;
    }

    /// <summary>
    /// Extrae información del mensaje con validación.
    /// ✅ Refactorizado para recibir LoadedBusinessContext precargado.
    /// </summary>
    public async Task<ExtractionResult> ExtractWithValidationAsync(
        string userMessage,
        ConversationState currentState,
        Configuration.LoadedBusinessContext businessContext,
        CancellationToken cancellationToken)
    {
        try
        {
            // PASO 1: Intentar extracción con LLM (JSON Mode)
            var llmResult = await ExtractWithLLMAsync(userMessage, currentState, businessContext, cancellationToken);

            // PASO 2: Validar respuesta del LLM
            var validation = await _validator.ValidateExtractionAsync(llmResult, userMessage, currentState);

            if (validation.IsValid && validation.Confidence > 0.7)
            {
                var extractedFieldNames = string.Join(", ", llmResult.ExtractedFields.Select(f => f.FieldName));
                
                _logger.LogInformation(
                    "✅ Extracción exitosa vía LLM: {FieldCount} campos extraídos: [{Fields}], confidence: {Confidence:F2}",
                    llmResult.ExtractedFields.Count, extractedFieldNames, llmResult.Metadata.AverageConfidence);
                
                return new ExtractionResult
                {
                    Success = true,
                    Method = ExtractionMethod.LLM,
                    StructuredResponse = llmResult,
                    ValidationResult = validation
                };
            }

            // PASO 3: Si falló validación, intentar fallback
            _logger.LogWarning(
                "⚠️ Extracción LLM falló validación. Confidence: {Confidence:F2}. Usando fallback...",
                validation.Confidence);

            return await FallbackExtractionAsync(userMessage, currentState, businessContext.BusinessId, llmResult, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error en extracción inteligente, usando fallback de emergencia");
            return await EmergencyFallbackAsync(userMessage, currentState, cancellationToken);
        }
    }

    private async Task<StructuredExtractionResponse> ExtractWithLLMAsync(
        string userMessage,
        ConversationState currentState,
        Configuration.LoadedBusinessContext businessContext,
        CancellationToken cancellationToken)
    {
        // ✅ Pasar contexto precargado al builder (sin queries adicionales)
        var prompt = await _promptBuilder.BuildExtractionPromptAsync(
            businessContext, userMessage, currentState, cancellationToken);

        // TÉCNICA: System con instrucciones + User con mensaje
        // Prompt completo ya incluye toda la información necesaria (FieldDefinitions + FinalVerification + Examples)
        var request = new LLMRequest
        {
            Messages = new List<LLMMessage>
            {
                new() { Role = LLMRole.System, Content = prompt },
                new() { Role = LLMRole.User, Content = userMessage }
            },
            Temperature = 0.1f, // MÁS determinista
            MaxTokens = 1000
        };

        var response = await _llmAdapter.SendWithJsonModeAsync(request, cancellationToken);

        if (!response.Success)
        {
            throw new InvalidOperationException($"LLM falló: {response.ErrorMessage}");
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(
                    System.Text.Json.JsonNamingPolicy.CamelCase, 
                    allowIntegerValues: false) }
            };

            var structured = JsonSerializer.Deserialize<StructuredExtractionResponse>(
                response.Content,
                options);

            if (structured == null)
            {
                throw new InvalidOperationException("LLM retornó JSON inválido o null");
            }

            // Validar y normalizar field_types que puedan venir en formato incorrecto
            foreach (var field in structured.ExtractedFields)
            {
                // Si el field_type no se pudo deserializar correctamente, intentar inferirlo
                if (field.FieldType == default(FieldType) && !string.IsNullOrEmpty(field.Value))
                {
                    field.FieldType = InferFieldType(field.FieldName, field.Value);
                    _logger.LogDebug(
                        "FieldType inferido para {FieldName}: {InferredType}",
                        field.FieldName, field.FieldType);
                }
            }

            return structured;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, 
                "Error deserializando JSON del LLM. JSON recibido: {JsonContent}", 
                response.Content);
            throw;
        }
    }

    private async Task<ExtractionResult> FallbackExtractionAsync(
        string userMessage,
        ConversationState currentState,
        Guid businessId,
        StructuredExtractionResponse llmAttempt,
        CancellationToken cancellationToken)
    {
        var fallbackResult = await _fallbackExtractor.ExtractAsync(
            userMessage, currentState, businessId, llmAttempt, cancellationToken);

        _logger.LogInformation(
            "🔄 Extracción fallback completada: {FieldCount} campos con confidence {Confidence:F2}",
            fallbackResult.ExtractedFields.Count,
            fallbackResult.Metadata.AverageConfidence);

        return new ExtractionResult
        {
            Success = true,
            Method = ExtractionMethod.Fallback,
            StructuredResponse = fallbackResult,
            ValidationResult = new ValidationResult { IsValid = true, Confidence = 0.6 }
        };
    }

    private Task<ExtractionResult> EmergencyFallbackAsync(
        string userMessage,
        ConversationState currentState,
        CancellationToken cancellationToken)
    {
        var emergencyResponse = new StructuredExtractionResponse
        {
            ExtractedFields = new List<ExtractedField>(),
            ConversationalResponse = LocalizationConstants.ErrorMessages.TechnicalDifficulty,
            FlowAnalysis = new FlowAnalysis(),
            Ambiguities = new List<AmbiguityDetection>(),
            Metadata = new ExtractionMetadata
            {
                IsComplete = false,
                NeedsClarification = true
            }
        };

        return Task.FromResult(new ExtractionResult
        {
            Success = false,
            Method = ExtractionMethod.Emergency,
            StructuredResponse = emergencyResponse,
            ValidationResult = new ValidationResult { IsValid = false, Confidence = 0.0 }
        });
    }

    /// <summary>
    /// Infiere el tipo de campo basado en el nombre del campo y su valor
    /// </summary>
    private FieldType InferFieldType(string fieldName, string value)
    {
        var fieldNameLower = fieldName.ToLowerInvariant();
        var valueLower = value.ToLowerInvariant();

        // Inferir por nombre del campo
        if (fieldNameLower.Contains("phone") || fieldNameLower.Contains("telefono") || fieldNameLower.Contains("celular"))
            return FieldType.Phone;

        if (fieldNameLower.Contains("email") || fieldNameLower.Contains("correo"))
            return FieldType.Email;

        if (fieldNameLower.Contains("date") || fieldNameLower.Contains("fecha"))
            return FieldType.Date;

        if (fieldNameLower.Contains("time") || fieldNameLower.Contains("hora"))
            return FieldType.Time;

        if (fieldNameLower.Contains("service") || fieldNameLower.Contains("servicio") || fieldNameLower.Contains("plan"))
            return FieldType.Service;

        if (fieldNameLower.Contains("age") || fieldNameLower.Contains("edad") || fieldNameLower.Contains("meses"))
            return FieldType.Number;

        // Inferir por valor
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+$"))
            return FieldType.Number;

        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$"))
            return FieldType.Date;

        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{2}:\d{2}$"))
            return FieldType.Time;

        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return FieldType.Email;

        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\+?[\d\s\-\(\)]+$") && value.Length >= 7)
            return FieldType.Phone;

        // Por defecto, texto
        return FieldType.Text;
    }
}
