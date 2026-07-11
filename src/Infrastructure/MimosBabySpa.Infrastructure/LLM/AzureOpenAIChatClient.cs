using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.LLM;
using OpenAI.Chat;
using SdkChatMessage = OpenAI.Chat.ChatMessage;

namespace MimosBabySpa.Infrastructure.LLM;

/// <summary>
/// Single adapter between the application contract and the Azure OpenAI SDK.
/// Supports function calling for the active engine and strict JSON Schema outputs for planners.
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
        IReadOnlyList<ChatToolDefinition>? tools = null,
        Application.LLM.ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (options?.StructuredOutput is not null && tools is { Count: > 0 })
                throw new InvalidOperationException("Structured output and function tools cannot be requested in the same planner call.");

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

            if (tools is { Count: > 0 } && options?.ForceTextResponse != true)
            {
                foreach (var tool in tools)
                {
                    sdkOptions.Tools.Add(ChatTool.CreateFunctionTool(
                        tool.Name,
                        tool.Description,
                        BinaryData.FromString(tool.ParametersJson)));
                }
            }

            _logger.LogDebug(
                "Chat completion: messages={MessageCount}, tools={ToolCount}, structured={Structured}, forceText={ForceText}",
                messages.Count,
                tools?.Count ?? 0,
                options?.StructuredOutput?.Name,
                options?.ForceTextResponse);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI chat completion failed");
            return new ChatCompletionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FinishReason = ChatCompletionFinishReason.Error,
                AssistantMessage = Application.LLM.ChatMessage.Assistant(string.Empty)
            };
        }
    }

    private static ChatCompletionResult BuildResult(ChatCompletion completion)
    {
        if (completion.ToolCalls.Count > 0)
        {
            var toolCalls = completion.ToolCalls
                .Select(call => new ToolCallRequest
                {
                    Id = call.Id,
                    FunctionName = call.FunctionName,
                    ArgumentsJson = call.FunctionArguments.ToString()
                })
                .ToList();

            return new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.ToolCalls,
                ToolCalls = toolCalls,
                AssistantMessage = Application.LLM.ChatMessage.AssistantWithToolCalls(toolCalls),
                PromptTokens = completion.Usage.InputTokenCount,
                CompletionTokens = completion.Usage.OutputTokenCount
            };
        }

        var content = string.Concat(completion.Content.Select(part => part.Text));
        var finishReason = completion.FinishReason switch
        {
            ChatFinishReason.Length => ChatCompletionFinishReason.Length,
            _ => ChatCompletionFinishReason.Stop
        };

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
        Application.LLM.ChatRole.Assistant when message.ToolCalls is { Count: > 0 } =>
            new AssistantChatMessage(message.ToolCalls.Select(call =>
                ChatToolCall.CreateFunctionToolCall(
                    call.Id,
                    call.FunctionName,
                    BinaryData.FromString(call.ArgumentsJson)))),
        Application.LLM.ChatRole.Assistant => new AssistantChatMessage(message.Content ?? string.Empty),
        Application.LLM.ChatRole.Tool => new ToolChatMessage(message.ToolCallId!, message.Content ?? string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(message), $"Unknown role: {message.Role}")
    };
}