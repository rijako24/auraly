namespace MimosBabySpa.Application.LLM;

/// <summary>
/// Abstracción del cliente de chat con soporte nativo de Function Calling.
/// Reemplaza a ILLMAdapter al incluir tools en el contrato.
/// </summary>
public interface IChatClient
{
    /// <summary>
    /// Envía una conversación al LLM con tools opcionales.
    /// La respuesta puede contener tool_calls o contenido final.
    /// </summary>
    Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Opciones de la llamada al modelo (override por turno).
/// </summary>
public sealed class ChatCompletionOptions
{
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Cuando es true, fuerza al modelo a responder en texto sin invocar tools.
    /// Usado para cortar loops o forzar respuesta final.
    /// </summary>
    public bool ForceTextResponse { get; init; }
}

/// <summary>
/// Resultado de una llamada al modelo.
/// </summary>
public sealed class ChatCompletionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ChatCompletionFinishReason FinishReason { get; init; }

    /// <summary>Contenido de texto cuando FinishReason es Stop.</summary>
    public string? Content { get; init; }

    /// <summary>Tool calls solicitadas por el modelo cuando FinishReason es ToolCalls.</summary>
    public IReadOnlyList<ToolCallRequest> ToolCalls { get; init; } = [];

    /// <summary>El mensaje de assistant completo para añadir al historial.</summary>
    public ChatMessage AssistantMessage { get; init; } = null!;

    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
}

public enum ChatCompletionFinishReason
{
    Stop,
    ToolCalls,
    Length,
    Error
}

/// <summary>
/// Tool call solicitada por el modelo.
/// </summary>
public sealed class ToolCallRequest
{
    public string Id { get; init; } = string.Empty;
    public string FunctionName { get; init; } = string.Empty;
    public string ArgumentsJson { get; init; } = string.Empty;
}

/// <summary>
/// Mensaje en el historial de conversación.
/// </summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }
    public string? Content { get; init; }
    public IReadOnlyList<ToolCallRequest>? ToolCalls { get; init; }

    /// <summary>Relleno para mensajes de tipo Tool (resultado de ejecución).</summary>
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }

    public static ChatMessage System(string content) => new() { Role = ChatRole.System, Content = content };
    public static ChatMessage User(string content) => new() { Role = ChatRole.User, Content = content };
    public static ChatMessage Assistant(string content) => new() { Role = ChatRole.Assistant, Content = content };
    public static ChatMessage AssistantWithToolCalls(IReadOnlyList<ToolCallRequest> toolCalls) =>
        new() { Role = ChatRole.Assistant, ToolCalls = toolCalls };
    public static ChatMessage Tool(string toolCallId, string toolName, string content) =>
        new() { Role = ChatRole.Tool, ToolCallId = toolCallId, ToolName = toolName, Content = content };
}

public enum ChatRole { System, User, Assistant, Tool }

/// <summary>
/// Definición de una tool para OpenAI Function Calling.
/// </summary>
public sealed class ChatToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>JSON Schema de los parámetros (RFC 7159).</summary>
    public string ParametersJson { get; init; } = "{}";
}
