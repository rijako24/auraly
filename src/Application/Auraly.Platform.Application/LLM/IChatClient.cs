namespace Auraly.Platform.Application.LLM;

/// <summary>
/// Text and strict structured-output client. The deterministic agent engine does
/// not expose function-calling through this contract.
/// </summary>
public interface IChatClient
{
    Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class ChatCompletionOptions
{
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public ChatStructuredOutput? StructuredOutput { get; init; }
}

public sealed class ChatStructuredOutput
{
    public string Name { get; init; } = string.Empty;
    public string JsonSchema { get; init; } = "{}";
    public string? Description { get; init; }
    public bool Strict { get; init; } = true;
}

public sealed class ChatCompletionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ChatCompletionFinishReason FinishReason { get; init; }
    public string? Content { get; init; }
    public ChatMessage AssistantMessage { get; init; } = null!;
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
}

public enum ChatCompletionFinishReason
{
    Stop,
    Length,
    Error
}

public sealed class ChatMessage
{
    public ChatRole Role { get; init; }
    public string? Content { get; init; }
    public byte[]? ImageBytes { get; init; }
    public string? ImageMimeType { get; init; }

    public static ChatMessage System(string content) => new() { Role = ChatRole.System, Content = content };
    public static ChatMessage User(string content) => new() { Role = ChatRole.User, Content = content };
    public static ChatMessage UserWithImage(string content, byte[] imageBytes, string imageMimeType) => new()
    {
        Role = ChatRole.User,
        Content = content,
        ImageBytes = imageBytes,
        ImageMimeType = imageMimeType
    };
    public static ChatMessage Assistant(string content) => new() { Role = ChatRole.Assistant, Content = content };
}

public enum ChatRole { System, User, Assistant }
