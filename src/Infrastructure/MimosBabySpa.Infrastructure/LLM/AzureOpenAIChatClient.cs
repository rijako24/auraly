using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.LLM;
using OpenAI.Chat;
using SdkChatMessage = OpenAI.Chat.ChatMessage;

namespace MimosBabySpa.Infrastructure.LLM;

/// <summary>
/// Single adapter for text and strict JSON Schema outputs. Operations are never
/// exposed to the model through the application contract.
/// </summary>
public sealed class AzureOpenAIChatClient : IChatClient
{
    private readonly ChatClient _client;
    private readonly ILogger<AzureOpenAIChatClient> _logger;

    public AzureOpenAIChatClient(
        AzureOpenAIClient client,
        string deploymentName,
        ILogger<AzureOpenAIChatClient> logger)
    {
        _client = client.GetChatClient(deploymentName);
        _logger = logger;
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<Application.LLM.ChatMessage> messages,
        Application.LLM.ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sdkOptions = new OpenAI.Chat.ChatCompletionOptions
            {
                Temperature = options?.Temperature ?? 0.7f,
                MaxOutputTokenCount = options?.MaxTokens ?? 800
            };

            if (options?.StructuredOutput is { } structured)
            {
                sdkOptions.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    structured.Name,
                    BinaryData.FromString(structured.JsonSchema),
                    structured.Description,
                    structured.Strict);
            }

            _logger.LogDebug(
                "Chat completion: messages={MessageCount}, structured={Structured}",
                messages.Count,
                options?.StructuredOutput?.Name);

            var response = await _client.CompleteChatAsync(
                messages.Select(ToSdkMessage),
                sdkOptions,
                cancellationToken);
            var completion = response.Value;

            _logger.LogInformation(
                "Chat completion done: finish_reason={FinishReason}, tokens={TotalTokens}",
                completion.FinishReason,
                completion.Usage.TotalTokenCount);

            return BuildResult(completion);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Azure OpenAI chat completion failed");
            return new ChatCompletionResult
            {
                Success = false,
                ErrorMessage = exception.Message,
                FinishReason = ChatCompletionFinishReason.Error,
                AssistantMessage = Application.LLM.ChatMessage.Assistant(string.Empty)
            };
        }
    }

    private static ChatCompletionResult BuildResult(ChatCompletion completion)
    {
        var content = string.Concat(completion.Content.Select(part => part.Text));
        var finishReason = completion.FinishReason == ChatFinishReason.Length
            ? ChatCompletionFinishReason.Length
            : ChatCompletionFinishReason.Stop;

        return new ChatCompletionResult
        {
            Success = true,
            FinishReason = finishReason,
            Content = content,
            AssistantMessage = Application.LLM.ChatMessage.Assistant(content),
            PromptTokens = completion.Usage.InputTokenCount,
            CompletionTokens = completion.Usage.OutputTokenCount
        };
    }

    private static SdkChatMessage ToSdkMessage(Application.LLM.ChatMessage message) => message.Role switch
    {
        Application.LLM.ChatRole.System => new SystemChatMessage(message.Content ?? string.Empty),
        Application.LLM.ChatRole.User => new UserChatMessage(message.Content ?? string.Empty),
        Application.LLM.ChatRole.Assistant => new AssistantChatMessage(message.Content ?? string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(message), $"Unknown role: {message.Role}")
    };
}
