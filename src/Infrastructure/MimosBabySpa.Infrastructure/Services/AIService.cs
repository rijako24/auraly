using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Infrastructure.Services;

public class AIService : IAIService
{
    private readonly OpenAIClient _openAIClient;
    private readonly string _textDeploymentName; // Para GPT (texto)
    private readonly string _audioDeploymentName; // Para Whisper (audio)
    private readonly IBusinessConfigurationService _businessConfigService;
    private readonly IConversationContextService _contextService;
    private readonly ILogger<AIService> _logger;

    public AIService(
        OpenAIClient openAIClient,
        string textDeploymentName,
        string audioDeploymentName,
        IBusinessConfigurationService businessConfigService,
        IConversationContextService contextService,
        ILogger<AIService> logger)
    {
        _openAIClient = openAIClient;
        _textDeploymentName = textDeploymentName;
        _audioDeploymentName = audioDeploymentName;
        _businessConfigService = businessConfigService;
        _contextService = contextService;
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(Guid businessId, string userMessage, Conversation? conversation, string intent, Lead? lead)
    {
        try
        {
            var chatMessages = new List<ChatRequestMessage>();

            // Construir system prompt dinámicamente desde la configuración del negocio
            var systemPrompt = await _businessConfigService.BuildSystemPromptAsync(businessId);
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                chatMessages.Add(new ChatRequestSystemMessage(systemPrompt));
            }

            // Contexto de la conversación (dinámico desde ConversationContext)
            if (conversation != null)
            {
                var contextMessage = await _contextService.BuildContextMessageAsync(conversation.ConversationId, businessId);
                if (!string.IsNullOrEmpty(contextMessage))
                {
                    chatMessages.Add(new ChatRequestSystemMessage(contextMessage));
                }
            }

            // Historial de mensajes recientes (últimos 5)
            if (conversation?.Messages != null && conversation.Messages.Any())
            {
                var recentMessages = conversation.Messages
                    .OrderByDescending(m => m.Timestamp)
                    .Take(5)
                    .OrderBy(m => m.Timestamp)
                    .ToList();

                foreach (var msg in recentMessages)
                {
                    var role = msg.Sender == "User" ? ChatRole.User : ChatRole.Assistant;
                    chatMessages.Add(new ChatRequestUserMessage(msg.MessageText));
                    if (role == ChatRole.Assistant)
                    {
                        chatMessages.Add(new ChatRequestAssistantMessage(msg.MessageText));
                    }
                }
            }

            // Mensaje actual del usuario
            chatMessages.Add(new ChatRequestUserMessage(userMessage));

            // Reglas según la intención (desde BusinessConfiguration)
            var intentRules = await GetIntentRulesAsync(businessId, intent);
            if (!string.IsNullOrEmpty(intentRules))
            {
                chatMessages.Add(new ChatRequestSystemMessage(intentRules));
            }

            var options = new ChatCompletionsOptions(_textDeploymentName, chatMessages)
            {
                Temperature = 0.7f,
                MaxTokens = 500
            };

            var response = await _openAIClient.GetChatCompletionsAsync(options);
            var generatedText = response.Value.Choices[0].Message.Content;

            _logger.LogDebug("Respuesta generada para intención: {Intent}", intent);
            return generatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al generar respuesta con IA");
            return "Disculpa, estoy teniendo dificultades técnicas. ¿Podrías repetir tu pregunta?";
        }
    }

    public async Task<string> ClassifyIntentAsync(string messageText, Conversation? conversation)
    {
        try
        {
            var businessId = conversation?.BusinessId ?? Guid.Empty;
            if (businessId == Guid.Empty)
            {
                _logger.LogWarning("No se puede clasificar intención sin BusinessId");
                return "FollowUp";
            }

            // Obtener todas las configuraciones del negocio de una vez
            var businessConfig = await _businessConfigService.GetConfigurationAsync(businessId);
            
            // Obtener el template unificado desde SystemConfiguration (una sola llamada)
            var unifiedPromptTemplate = await _businessConfigService.GetSystemConfigurationAsync(SystemConfigurationKey.ContextExtractionPrompt);
            
            // Extraer las definiciones de intenciones desde IntentRules (las claves del JSON)
            var intentDefinitions = ExtractIntentDefinitionsFromRules(businessConfig);
            
            // Extraer configuraciones necesarias para el template
            var contextData = businessConfig.HasKey(Domain.Enums.BusinessConfigurationKey.ContextData)
                ? businessConfig.GetValue(Domain.Enums.BusinessConfigurationKey.ContextData)
                : string.Empty;
            var generalInfo = businessConfig.HasKey(Domain.Enums.BusinessConfigurationKey.GeneralInformation)
                ? businessConfig.GetValue(Domain.Enums.BusinessConfigurationKey.GeneralInformation)
                : string.Empty;
            var planRules = businessConfig.HasKey(Domain.Enums.BusinessConfigurationKey.PlanRules)
                ? businessConfig.GetValue(Domain.Enums.BusinessConfigurationKey.PlanRules)
                : string.Empty;
            
            // Construir el prompt completo reemplazando todos los placeholders
            var classificationPrompt = unifiedPromptTemplate
                .Replace("{intentDefinitions}", intentDefinitions)
                .Replace("{messageText}", messageText)
                .Replace("{contextData}", contextData)
                .Replace("{generalInfo}", generalInfo)
                .Replace("{planRules}", planRules);

            var chatMessages = new List<ChatRequestMessage>
            {
                new ChatRequestSystemMessage("Eres un analizador inteligente. Clasifica la intención del mensaje y extrae información relevante del contexto. Responde SOLO con JSON válido, sin explicaciones adicionales."),
                new ChatRequestUserMessage(classificationPrompt)
            };

            var options = new ChatCompletionsOptions(_textDeploymentName, chatMessages)
            {
                Temperature = 0.3f,
                MaxTokens = 400,
                ResponseFormat = ChatCompletionsResponseFormat.JsonObject
            };

            var response = await _openAIClient.GetChatCompletionsAsync(options);
            var aiResponse = response.Value.Choices[0].Message.Content.Trim();
            
            // Parsear la respuesta JSON para extraer solo la intención
            var intent = "FollowUp";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(aiResponse);
                if (doc.RootElement.TryGetProperty("intent", out var intentElement))
                {
                    intent = intentElement.GetString() ?? "FollowUp";
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Si falla el parseo, intentar extraer la intención del texto directamente
                intent = aiResponse.Trim();
            }

            // Validar que la intención sea válida
            var validIntents = Enum.GetNames(typeof(MessageIntent));
            if (validIntents.Contains(intent, StringComparer.OrdinalIgnoreCase))
            {
                return intent;
            }

            // Si no coincide exactamente, intentar encontrar la más cercana
            var matchedIntent = validIntents.FirstOrDefault(i =>
                intent.Contains(i, StringComparison.OrdinalIgnoreCase) ||
                i.Contains(intent, StringComparison.OrdinalIgnoreCase));

            return matchedIntent ?? "FollowUp";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al clasificar intención");
            return "FollowUp";
        }
    }

    public async Task<IntentAndContextResult> ClassifyIntentAndExtractContextAsync(Guid businessId, string messageText, Conversation? conversation)
    {
        try
        {
            // Obtener todas las configuraciones del negocio de una vez
            var businessConfig = await _businessConfigService.GetConfigurationAsync(businessId);

            // Obtener el template unificado desde SystemConfiguration (una sola llamada)
            var unifiedPromptTemplate = await _businessConfigService.GetSystemConfigurationAsync(SystemConfigurationKey.ContextExtractionPrompt);
            
            // Extraer las definiciones de intenciones desde IntentRules (las claves del JSON)
            var intentDefinitions = ExtractIntentDefinitionsFromRules(businessConfig);

            // Extraer las configuraciones necesarias
            var contextData = businessConfig.HasKey(Domain.Enums.BusinessConfigurationKey.ContextData)
                ? businessConfig.GetValue(Domain.Enums.BusinessConfigurationKey.ContextData)
                : string.Empty;
            var generalInfo = businessConfig.HasKey(Domain.Enums.BusinessConfigurationKey.GeneralInformation)
                ? businessConfig.GetValue(Domain.Enums.BusinessConfigurationKey.GeneralInformation)
                : string.Empty;
            var planRules = businessConfig.HasKey(Domain.Enums.BusinessConfigurationKey.PlanRules)
                ? businessConfig.GetValue(Domain.Enums.BusinessConfigurationKey.PlanRules)
                : string.Empty;

            // Reemplazar todos los placeholders en el prompt unificado
            var unifiedPrompt = unifiedPromptTemplate
                .Replace("{intentDefinitions}", intentDefinitions)
                .Replace("{messageText}", messageText)
                .Replace("{contextData}", contextData)
                .Replace("{generalInfo}", generalInfo)
                .Replace("{planRules}", planRules);

            var chatMessages = new List<ChatRequestMessage>
            {
                new ChatRequestSystemMessage("Eres un analizador inteligente. Clasifica la intención del mensaje y extrae información relevante del contexto. Responde SOLO con JSON válido, sin explicaciones adicionales."),
                new ChatRequestUserMessage(unifiedPrompt)
            };

            var options = new ChatCompletionsOptions(_textDeploymentName, chatMessages)
            {
                Temperature = 0.3f,
                MaxTokens = 400,
                ResponseFormat = ChatCompletionsResponseFormat.JsonObject
            };

            var response = await _openAIClient.GetChatCompletionsAsync(options);
            var aiResponse = response.Value.Choices[0].Message.Content.Trim();

            // Parsear la respuesta JSON
            var result = new IntentAndContextResult();

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(aiResponse);

                // Extraer intención
                if (doc.RootElement.TryGetProperty("intent", out var intentElement))
                {
                    var intent = intentElement.GetString() ?? "FollowUp";

                    // Validar que la intención sea válida
                    var validIntents = Enum.GetNames(typeof(MessageIntent));
                    if (validIntents.Contains(intent, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Intent = intent;
                    }
                    else
                    {
                        // Si no coincide exactamente, intentar encontrar la más cercana
                        var matchedIntent = validIntents.FirstOrDefault(i =>
                            intent.Contains(i, StringComparison.OrdinalIgnoreCase) ||
                            i.Contains(intent, StringComparison.OrdinalIgnoreCase));
                        result.Intent = matchedIntent ?? "FollowUp";
                    }
                }
                else
                {
                    result.Intent = "FollowUp";
                }

                // Extraer contexto como lista de strings
                if (doc.RootElement.TryGetProperty("context", out var contextElement) && contextElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    // Si es un array de strings
                    foreach (var item in contextElement.EnumerateArray())
                    {
                        var contextString = item.GetString();
                        if (!string.IsNullOrEmpty(contextString))
                        {
                            result.Context.Add(contextString);
                        }
                    }
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Error al parsear respuesta JSON de la IA. Respuesta: {Response}", aiResponse);
                // Fallback: intentar parsear solo la intención
                result.Intent = await ClassifyIntentAsync(messageText, conversation);
            }

            _logger.LogDebug("IA clasificó intención: {Intent} y extrajo contexto: {Fields}",
                result.Intent,
                string.Join(", ", result.Context));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al clasificar intención y extraer contexto");
            // Fallback: solo clasificar intención
            var intent = await ClassifyIntentAsync(messageText, conversation);
            return new IntentAndContextResult
            {
                Intent = intent,
                Context = new List<string>()
            };
        }
    }



    private string ExtractIntentDefinitionsFromRules(BusinessConfigurationDto businessConfig)
    {
        // Obtener IntentRules desde BusinessConfiguration
        var intentRulesJson = businessConfig.HasKey(Domain.Enums.BusinessConfigurationKey.IntentRules)
            ? businessConfig.GetValue(Domain.Enums.BusinessConfigurationKey.IntentRules)
            : string.Empty;

        if (string.IsNullOrEmpty(intentRulesJson))
        {
            _logger.LogWarning("IntentRules no está configurado en BusinessConfiguration. No se pueden extraer definiciones de intenciones.");
            return string.Empty;
        }

        try
        {
            // Parsear el JSON y extraer las claves (nombres de intenciones)
            using var doc = System.Text.Json.JsonDocument.Parse(intentRulesJson);
            var intentList = new List<string>();

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                intentList.Add($"- {property.Name}");
            }

            return string.Join("\n", intentList);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Error al parsear IntentRules JSON. El formato del JSON es inválido.");
            return string.Empty;
        }
    }

    private async Task<string> GetIntentRulesAsync(Guid businessId, string intent)
    {
        try
        {
            // Obtener las reglas de intención desde BusinessConfiguration
            var intentRulesJson = await _businessConfigService.GetBusinessConfigurationValueAsync(
                businessId, 
                Domain.Enums.BusinessConfigurationKey.IntentRules);

            if (string.IsNullOrEmpty(intentRulesJson))
            {
                _logger.LogWarning("IntentRules no está configurado para el negocio {BusinessId}. No se puede obtener regla para intención {Intent}.", businessId, intent);
                return string.Empty;
            }

            // Parsear el JSON con las reglas
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(intentRulesJson);
                if (doc.RootElement.TryGetProperty(intent, out var intentRule))
                {
                    var rule = intentRule.GetString();
                    if (string.IsNullOrEmpty(rule))
                    {
                        _logger.LogWarning("La regla para la intención {Intent} está vacía en IntentRules para el negocio {BusinessId}.", intent, businessId);
                        return string.Empty;
                    }
                    return rule;
                }
                else
                {
                    _logger.LogWarning("La intención {Intent} no se encuentra en IntentRules para el negocio {BusinessId}.", intent, businessId);
                    return string.Empty;
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "Error al parsear IntentRules JSON para negocio {BusinessId}. El formato del JSON es inválido.", businessId);
                return string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener reglas de intención para negocio {BusinessId}", businessId);
            return string.Empty;
        }
    }

    public async Task<string> TranscribeAudioAsync(Stream audioStream, string mimeType)
    {
        try
        {
            _logger.LogInformation("Transcribiendo audio con tipo MIME: {MimeType}", mimeType);

            // Convertir el stream a bytes
            using var memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream);
            var audioBytes = memoryStream.ToArray();

            // Llamar a la API de Azure OpenAI Whisper para transcribir
            // IMPORTANTE: En la versión beta, el deployment name se pasa dentro de AudioTranscriptionOptions
            var response = await _openAIClient.GetAudioTranscriptionAsync(
                new AudioTranscriptionOptions(_audioDeploymentName, BinaryData.FromBytes(audioBytes))
                {
                    ResponseFormat = AudioTranscriptionFormat.Verbose,
                    Language = "es" // Español, puedes hacerlo configurable
                });

            var transcription = response.Value.Text;
            _logger.LogInformation("Audio transcrito: {Transcription}", transcription);

            return transcription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al transcribir audio");
            throw;
        }
    }
}
