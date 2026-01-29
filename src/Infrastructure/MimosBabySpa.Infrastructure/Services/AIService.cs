using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Infrastructure.Services;

public class AIService : IAIService
{
    private readonly OpenAIClient _openAIClient;
    private readonly string _textDeploymentName; // Para GPT (texto)
    private readonly string _audioDeploymentName; // Para Whisper (audio)
    private readonly IPromptProvider _systemPromptProvider;
    private readonly CachedBusinessContextProvider _cachedContextProvider;
    private readonly ILogger<AIService> _logger;

    public AIService(
        OpenAIClient openAIClient,
        string textDeploymentName,
        string audioDeploymentName,
        IPromptProvider systemPromptProvider,
        CachedBusinessContextProvider cachedContextProvider,
        ILogger<AIService> logger)
    {
        _openAIClient = openAIClient;
        _textDeploymentName = textDeploymentName;
        _audioDeploymentName = audioDeploymentName;
        _systemPromptProvider = systemPromptProvider;
        _cachedContextProvider = cachedContextProvider;
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(Guid businessId, string userMessage, Conversation? conversation, string intent, Lead? lead)
    {
        try
        {
            var chatMessages = new List<ChatRequestMessage>();

            // Construir system prompt usando el nuevo sistema (SystemPromptProvider + LoadedBusinessContext)
            var businessContext = await _cachedContextProvider.GetOrLoadAsync(businessId);
            var systemPrompt = await _systemPromptProvider.BuildAsync(businessContext);
            
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                chatMessages.Add(new ChatRequestSystemMessage(systemPrompt));
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
                    if (msg.Sender == "User")
                    {
                        chatMessages.Add(new ChatRequestUserMessage(msg.MessageText));
                    }
                    else if (msg.Sender == "Bot" || msg.Sender == "Assistant")
                    {
                        chatMessages.Add(new ChatRequestAssistantMessage(msg.MessageText));
                    }
                }
            }

            // Mensaje actual del usuario
            chatMessages.Add(new ChatRequestUserMessage(userMessage));

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

    public async Task<string> ProcessCustomPromptAsync(string systemPrompt, string userPrompt, bool jsonResponse = false, float temperature = 0.3f, int maxTokens = 400)
    {
        try
        {
            var chatMessages = new List<ChatRequestMessage>
            {
                new ChatRequestSystemMessage(systemPrompt),
                new ChatRequestUserMessage(userPrompt)
            };

            var options = new ChatCompletionsOptions(_textDeploymentName, chatMessages)
            {
                Temperature = temperature,
                MaxTokens = maxTokens
            };

            if (jsonResponse)
            {
                options.ResponseFormat = ChatCompletionsResponseFormat.JsonObject;
            }

            var response = await _openAIClient.GetChatCompletionsAsync(options);
            return response.Value.Choices[0].Message.Content.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando prompt personalizado");
            throw;
        }
    }
}
