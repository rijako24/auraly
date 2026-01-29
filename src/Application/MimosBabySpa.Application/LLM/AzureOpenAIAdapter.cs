using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.LLM;

/// <summary>
/// Adaptador para Azure OpenAI.
/// Implementa la interfaz genérica ILLMAdapter usando Azure OpenAI SDK.
/// </summary>
public class AzureOpenAIAdapter : ILLMAdapter
{
    private readonly OpenAIClient _client;
    private readonly string _deploymentName;
    private readonly ILogger<AzureOpenAIAdapter> _logger;

    public AzureOpenAIAdapter(
        OpenAIClient client,
        string deploymentName,
        ILogger<AzureOpenAIAdapter> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _deploymentName = deploymentName ?? throw new ArgumentNullException(nameof(deploymentName));
        _logger = logger;
    }

    public async Task<LLMResponse> SendMessageAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = ConvertMessages(request.Messages);
            
            var options = new ChatCompletionsOptions(_deploymentName, messages)
            {
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens
            };

            _logger.LogDebug(
                "Enviando mensaje a Azure OpenAI: Messages={Count}, Temperature={Temp}, MaxTokens={MaxTokens}",
                messages.Count, request.Temperature, request.MaxTokens);

            var response = await _client.GetChatCompletionsAsync(options, cancellationToken);
            var choice = response.Value.Choices[0];

            var llmResponse = new LLMResponse
            {
                Content = choice.Message.Content ?? string.Empty,
                Success = true,
                FinishReason = choice.FinishReason?.ToString(),
                Usage = new TokenUsage
                {
                    PromptTokens = response.Value.Usage.PromptTokens,
                    CompletionTokens = response.Value.Usage.CompletionTokens,
                    TotalTokens = response.Value.Usage.TotalTokens
                }
            };

            _logger.LogInformation(
                "Respuesta recibida: Tokens={Total} (Prompt={Prompt}, Completion={Completion}), " +
                "FinishReason={FinishReason}",
                llmResponse.Usage.TotalTokens, llmResponse.Usage.PromptTokens, 
                llmResponse.Usage.CompletionTokens, llmResponse.FinishReason);

            return llmResponse;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Error en Azure OpenAI: {Message}", ex.Message);
            
            return new LLMResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al llamar Azure OpenAI");
            
            return new LLMResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ========================================
    // MÉTODOS PRIVADOS HELPER
    // ========================================

    private List<ChatRequestMessage> ConvertMessages(List<LLMMessage> messages)
    {
        var converted = new List<ChatRequestMessage>();

        foreach (var msg in messages)
        {
            converted.Add(msg.Role switch
            {
                LLMRole.System => new ChatRequestSystemMessage(msg.Content),
                LLMRole.User => new ChatRequestUserMessage(msg.Content),
                LLMRole.Assistant => new ChatRequestAssistantMessage(msg.Content),
                _ => throw new ArgumentException($"Rol desconocido: {msg.Role}")
            });
        }

        return converted;
    }

    public async Task<LLMResponse> SendWithJsonModeAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = ConvertMessages(request.Messages);
            
            var options = new ChatCompletionsOptions(_deploymentName, messages)
            {
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
                ResponseFormat = ChatCompletionsResponseFormat.JsonObject
            };

            _logger.LogDebug(
                "Enviando mensaje con JSON Mode: Messages={Count}, Temperature={Temp}",
                messages.Count, request.Temperature);

            var response = await _client.GetChatCompletionsAsync(options, cancellationToken);
            var choice = response.Value.Choices[0];

            var llmResponse = new LLMResponse
            {
                Content = choice.Message.Content ?? string.Empty,
                Success = true,
                FinishReason = choice.FinishReason?.ToString(),
                Usage = new TokenUsage
                {
                    PromptTokens = response.Value.Usage.PromptTokens,
                    CompletionTokens = response.Value.Usage.CompletionTokens,
                    TotalTokens = response.Value.Usage.TotalTokens
                }
            };

            _logger.LogInformation(
                "Respuesta JSON recibida: Tokens={Total}, FinishReason={FinishReason}",
                llmResponse.Usage.TotalTokens, llmResponse.FinishReason);

            return llmResponse;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Error en Azure OpenAI JSON Mode: {Message}", ex.Message);
            
            return new LLMResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado en JSON Mode");
            
            return new LLMResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
