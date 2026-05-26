using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Infrastructure.LLM;

/// <summary>
/// Implementación de IChatClient sobre Azure OpenAI SDK beta.17.
/// Único punto de contacto con el SDK. Soporta Function Calling nativo.
/// </summary>
public sealed class AzureOpenAIChatClient : IChatClient
{
    private readonly OpenAIClient _client;
    private readonly string _deploymentName;
    private readonly ILogger<AzureOpenAIChatClient> _logger;

    public AzureOpenAIChatClient(
        OpenAIClient client,
        string deploymentName,
        ILogger<AzureOpenAIChatClient> logger)
    {
        _client = client;
        _deploymentName = deploymentName;
        _logger = logger;
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sdkMessages = messages.Select(ToSdkMessage).ToList();
            var deployment = string.IsNullOrWhiteSpace(options?.DeploymentNameOverride)
                ? _deploymentName
                : options!.DeploymentNameOverride!.Trim();

            var sdkOptions = new ChatCompletionsOptions(deployment, sdkMessages)
            {
                Temperature = options?.Temperature ?? 0.7f,
                MaxTokens = options?.MaxTokens ?? 800
            };

            if (options?.ForceJsonResponse == true)
                sdkOptions.ResponseFormat = ChatCompletionsResponseFormat.JsonObject;

            if (tools is { Count: > 0 } && options?.ForceTextResponse != true)
            {
                foreach (var tool in tools)
                    sdkOptions.Tools.Add(ToSdkTool(tool));

                sdkOptions.ToolChoice = ChatCompletionsToolChoice.Auto;
            }

            _logger.LogDebug(
                "Chat completion: messages={MsgCount}, tools={ToolCount}, forceText={Force}, forceJson={Json}",
                messages.Count, tools?.Count ?? 0, options?.ForceTextResponse, options?.ForceJsonResponse);

            var response = await _client.GetChatCompletionsAsync(sdkOptions, cancellationToken);
            var choice = response.Value.Choices[0];

            _logger.LogInformation(
                "Chat completion done: finish_reason={Finish}, tokens={Total}",
                choice.FinishReason, response.Value.Usage.TotalTokens);

            return BuildResult(choice, response.Value.Usage);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure OpenAI error {Status}: {Message}", ex.Status, ex.Message);
            return new ChatCompletionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FinishReason = ChatCompletionFinishReason.Error,
                AssistantMessage = ChatMessage.Assistant(string.Empty)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Azure OpenAI");
            return new ChatCompletionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FinishReason = ChatCompletionFinishReason.Error,
                AssistantMessage = ChatMessage.Assistant(string.Empty)
            };
        }
    }

    private static ChatCompletionResult BuildResult(ChatChoice choice, CompletionsUsage usage)
    {
        var finishReason = choice.FinishReason?.ToString() switch
        {
            "tool_calls" => ChatCompletionFinishReason.ToolCalls,
            "length" => ChatCompletionFinishReason.Length,
            _ => ChatCompletionFinishReason.Stop
        };

        if (finishReason == ChatCompletionFinishReason.ToolCalls)
        {
            var toolCalls = choice.Message.ToolCalls
                .OfType<ChatCompletionsFunctionToolCall>()
                .Select(tc => new ToolCallRequest
                {
                    Id = tc.Id,
                    FunctionName = tc.Name,
                    ArgumentsJson = tc.Arguments
                })
                .ToList();

            var assistantMsg = ChatMessage.AssistantWithToolCalls(toolCalls);

            return new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.ToolCalls,
                ToolCalls = toolCalls,
                AssistantMessage = assistantMsg,
                PromptTokens = usage.PromptTokens,
                CompletionTokens = usage.CompletionTokens
            };
        }

        var content = choice.Message.Content ?? string.Empty;
        return new ChatCompletionResult
        {
            Success = true,
            FinishReason = finishReason,
            Content = content,
            AssistantMessage = ChatMessage.Assistant(content),
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens
        };
    }

    private static ChatRequestMessage ToSdkMessage(ChatMessage msg) => msg.Role switch
    {
        Application.LLM.ChatRole.System => new ChatRequestSystemMessage(msg.Content ?? string.Empty),
        Application.LLM.ChatRole.User => new ChatRequestUserMessage(msg.Content ?? string.Empty),
        Application.LLM.ChatRole.Assistant when msg.ToolCalls is { Count: > 0 } => BuildAssistantWithToolCalls(msg),
        Application.LLM.ChatRole.Assistant => new ChatRequestAssistantMessage(msg.Content ?? string.Empty),
        Application.LLM.ChatRole.Tool => new ChatRequestToolMessage(msg.Content ?? string.Empty, msg.ToolCallId!),
        _ => throw new ArgumentOutOfRangeException(nameof(msg), $"Unknown role: {msg.Role}")
    };

    private static ChatRequestAssistantMessage BuildAssistantWithToolCalls(ChatMessage msg)
    {
        var sdkMsg = new ChatRequestAssistantMessage(msg.Content ?? string.Empty);
        foreach (var tc in msg.ToolCalls!)
            sdkMsg.ToolCalls.Add(new ChatCompletionsFunctionToolCall(tc.Id, tc.FunctionName, tc.ArgumentsJson));
        return sdkMsg;
    }

    private static ChatCompletionsFunctionToolDefinition ToSdkTool(ChatToolDefinition tool) =>
        new(new FunctionDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = BinaryData.FromString(tool.ParametersJson)
        });
}
