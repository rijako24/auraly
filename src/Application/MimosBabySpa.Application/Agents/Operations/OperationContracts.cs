using System.Text.Json;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents.Operations;

public sealed record OperationDescriptor(
    string Id,
    string InputSchema,
    IReadOnlyList<string> OutcomeCodes,
    IReadOnlyList<string> MutationScopes,
    IReadOnlyList<string> RequiredTemplateIds,
    IReadOnlyList<string> OperatingGroups);

public sealed class OperationContext
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public Guid ConversationId { get; init; }
    public DateOnly BusinessToday { get; init; }
    public DateTimeOffset BusinessNow { get; init; }
    public AgentConfig Config { get; init; } = null!;
    public ConversationState ConversationState { get; init; } = null!;
    public IReadOnlyDictionary<string, string> Facts { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public AgentConversationContext? Session { get; init; }
}

public interface IAgentOperation
{
    OperationDescriptor Descriptor { get; }

    Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record OperationError(
    string Code,
    string Message,
    bool Recoverable,
    string? RemediationSignal = null,
    JsonElement? Context = null);

public sealed record OperationPresentation(
    string TemplateId,
    IReadOnlyDictionary<string, object?> Data,
    FragmentRenderMode Mode = FragmentRenderMode.Inline,
    FragmentPriority Priority = FragmentPriority.Optional);

public abstract record OperationEffect(string Type);

public sealed record CompleteRequestOperationEffect() : OperationEffect("request.complete");
public sealed record EscalateHumanOperationEffect() : OperationEffect("escalation.human");
public sealed record ResetRequestOperationEffect(IReadOnlyList<string> ClearedFacts)
    : OperationEffect("request.reset");


public sealed record SaveVerificationEffect(
    string VerificationType,
    IReadOnlyDictionary<string, string> Dependencies,
    TimeSpan? Ttl)
    : OperationEffect("verification.save");

public sealed record OperationEvent(string Name, JsonElement Payload)
{
    public static OperationEvent Create(string name, object? payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return new OperationEvent(name, document.RootElement.Clone());
    }
}

public sealed record OperationOutcome
{
    public bool Success { get; init; }
    public string Code { get; init; } = string.Empty;
    public JsonElement Data { get; init; }
    public IReadOnlyList<OperationEvent> DomainEvents { get; init; } = [];
    public OperationError? Error { get; init; }
    public IReadOnlyList<OperationPresentation> Presentations { get; init; } = [];
    public IReadOnlyList<OperationEffect> Effects { get; init; } = [];
    public IReadOnlyList<string> Events { get; init; } = [];

    public static OperationOutcome Ok(
        string code,
        object? data,
        IReadOnlyList<OperationPresentation>? presentations = null,
        IReadOnlyList<OperationEffect>? effects = null,
        IReadOnlyList<string>? events = null,
        IReadOnlyList<OperationEvent>? domainEvents = null) =>
        new()
        {
            Success = true,
            Code = code,
            Data = ToElement(data),
            Presentations = presentations ?? [],
            Effects = effects ?? [],
            Events = events ?? [],
            DomainEvents = domainEvents ?? []
        };

    public static OperationOutcome Fail(
        string code,
        string message,
        bool recoverable = false,
        string? remediationSignal = null,
        object? context = null) =>
        new()
        {
            Success = false,
            Code = code,
            Data = ToElement(null),
            Error = new OperationError(
                code,
                message,
                recoverable,
                remediationSignal,
                context is null ? null : ToElement(context))
        };

    private static JsonElement ToElement(object? value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }
}
