using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// IChatClient falso que devuelve respuestas pre-programadas en secuencia.
/// Cada llamada a CompleteAsync extrae el siguiente resultado de la cola.
/// Si la cola se agota, devuelve un Stop genÃ©rico.
/// </summary>
public class FakeChatClient : IChatClient
{
    private readonly Queue<ChatCompletionResult> _queue;

    public int CallCount { get; private set; }

    public FakeChatClient(IEnumerable<ChatCompletionResult> scriptedResults)
    {
        _queue = new Queue<ChatCompletionResult>(scriptedResults);
    }

    public Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (_queue.Count > 0)
            return Task.FromResult(_queue.Dequeue());

        const string fallback = "Claro, te ayudo con lo que necesitas.";
        return Task.FromResult(new ChatCompletionResult
        {
            Success = true,
            FinishReason = ChatCompletionFinishReason.Stop,
            Content = fallback,
            AssistantMessage = ChatMessage.Assistant(fallback)
        });
    }

    /// <summary>Agrega mÃ¡s resultados scripteados a la cola (Ãºtil por turno).</summary>
    public void Enqueue(IEnumerable<ChatCompletionResult> results)
    {
        foreach (var r in results)
            _queue.Enqueue(r);
    }
}

/// <summary>
/// DSL para construir secuencias de resultados LLM en tests.
/// </summary>
public static class FakeLlmScript
{
    private static int _callIdCounter;

    /// <summary>LLM invoca una tool y luego retorna texto final.</summary>
    public static IReadOnlyList<ChatCompletionResult> ToolThenText(
        string toolName, string argsJson, string textResponse)
    {
        var callId = $"call_{++_callIdCounter:D3}";
        var toolCall = new ToolCallRequest { Id = callId, FunctionName = toolName, ArgumentsJson = argsJson };
        return
        [
            new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.ToolCalls,
                ToolCalls = [toolCall],
                AssistantMessage = ChatMessage.AssistantWithToolCalls([toolCall])
            },
            new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.Stop,
                Content = textResponse,
                AssistantMessage = ChatMessage.Assistant(textResponse)
            }
        ];
    }

    /// <summary>LLM invoca dos tools en paralelo y luego retorna texto final.</summary>
    public static IReadOnlyList<ChatCompletionResult> TwoToolsThenText(
        string tool1, string args1,
        string tool2, string args2,
        string textResponse)
    {
        var id1 = $"call_{++_callIdCounter:D3}";
        var id2 = $"call_{++_callIdCounter:D3}";
        var toolCalls = new List<ToolCallRequest>
        {
            new() { Id = id1, FunctionName = tool1, ArgumentsJson = args1 },
            new() { Id = id2, FunctionName = tool2, ArgumentsJson = args2 }
        };
        return
        [
            new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.ToolCalls,
                ToolCalls = toolCalls,
                AssistantMessage = ChatMessage.AssistantWithToolCalls(toolCalls)
            },
            new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.Stop,
                Content = textResponse,
                AssistantMessage = ChatMessage.Assistant(textResponse)
            }
        ];
    }

    public static IReadOnlyList<ChatCompletionResult> ManyToolsThenToolThenText(
        IReadOnlyList<(string ToolName, string ArgsJson)> firstTools,
        string nextToolName,
        string nextArgsJson,
        string textResponse)
    {
        var firstToolCalls = firstTools
            .Select(tool => new ToolCallRequest
            {
                Id = $"call_{++_callIdCounter:D3}",
                FunctionName = tool.ToolName,
                ArgumentsJson = tool.ArgsJson
            })
            .ToList();
        var nextToolCall = new ToolCallRequest
        {
            Id = $"call_{++_callIdCounter:D3}",
            FunctionName = nextToolName,
            ArgumentsJson = nextArgsJson
        };

        return
        [
            new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.ToolCalls,
                ToolCalls = firstToolCalls,
                AssistantMessage = ChatMessage.AssistantWithToolCalls(firstToolCalls)
            },
            new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.ToolCalls,
                ToolCalls = [nextToolCall],
                AssistantMessage = ChatMessage.AssistantWithToolCalls([nextToolCall])
            },
            new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.Stop,
                Content = textResponse,
                AssistantMessage = ChatMessage.Assistant(textResponse)
            }
        ];
    }

    /// <summary>LLM retorna directamente texto, sin llamar tools.</summary>
    public static IReadOnlyList<ChatCompletionResult> TextOnly(string text) =>
    [
        new ChatCompletionResult
        {
            Success = true,
            FinishReason = ChatCompletionFinishReason.Stop,
            Content = text,
            AssistantMessage = ChatMessage.Assistant(text)
        }
    ];
}
