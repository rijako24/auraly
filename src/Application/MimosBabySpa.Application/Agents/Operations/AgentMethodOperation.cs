using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Operations;

/// <summary>
/// Deterministic bridge for application methods whose implementation predates the operation
/// contract. The LLM never sees or invokes the underlying method; the configured stage owns
/// invocation and arguments. This bridge can be removed method-by-method without changing flows.
/// </summary>
public sealed class AgentMethodOperation : IAgentOperation
{
    private readonly IServiceProvider _services;
    private readonly string _methodName;
    private readonly string _successCode;

    public AgentMethodOperation(
        IServiceProvider services,
        string methodName,
        string operationId,
        string successCode,
        IReadOnlyList<string> outcomeCodes,
        string inputSchema,
        IReadOnlyList<string>? mutationScopes = null,
        IReadOnlyList<string>? requiredTemplateIds = null,
        IReadOnlyList<string>? operatingGroups = null)
    {
        _services = services;
        _methodName = methodName;
        _successCode = successCode;
        Descriptor = new OperationDescriptor(
            operationId,
            StrictInputSchema(inputSchema),
            outcomeCodes.Prepend(successCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            mutationScopes ?? [],
            requiredTemplateIds ?? [],
            operatingGroups ?? []);
    }

    public OperationDescriptor Descriptor { get; }

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var method = _services.GetRequiredService<AgentToolRegistry>().Resolve(_methodName)
            ?? throw new InvalidOperationException($"Configured application method '{_methodName}' is not registered.");
        var session = context.Session
            ?? throw new InvalidOperationException($"Operation '{Descriptor.Id}' requires a conversation session.");
        var previousTurn = session.Turn;
        var methodTurn = new AgentTurnExecution(context.Config.ConsecutiveErrorEscalationThreshold);
        session.Turn = methodTurn;
        try
        {
            var json = await method.ExecuteAsync(input, session, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var error = root.TryGetProperty("error", out var errorElement) ? errorElement : default;
                var code = ReadString(error, "code") ?? "method_failed";
                var message = ReadString(error, "message") ?? $"Operation '{Descriptor.Id}' failed.";
                var recoverable = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("recoverable", out var recoverableElement)
                    && recoverableElement.ValueKind == JsonValueKind.True;
                return OperationOutcome.Fail(code, message, recoverable, context: root.Clone());
            }

            var data = root.TryGetProperty("data", out var dataElement)
                ? dataElement.Clone()
                : EmptyObject();
            var presentations = methodTurn.FragmentEntries.Select(entry => new OperationPresentation(
                entry.Fragment.TemplateId,
                entry.Fragment.Data,
                entry.Fragment.Mode,
                entry.Fragment.Priority)).ToList();
            var events = ReadStrings(root, "events");
            var effects = ReadStrings(root, "effects").Select< string, OperationEffect?>(effect => effect switch
            {
                ToolSideEffectNames.RequestCompleted => new CompleteRequestOperationEffect(),
                ToolSideEffectNames.EscalatedToHuman => new EscalateHumanOperationEffect(),
                _ => null
            }).Where(effect => effect is not null).Cast<OperationEffect>().ToList();
            return OperationOutcome.Ok(
                ResolveSuccessCode(Descriptor.Id, data, _successCode),
                data,
                presentations,
                effects,
                events);
        }
        finally
        {
            session.Turn = previousTurn;
        }
    }

    private static string StrictInputSchema(string schema)
    {
        using var source = JsonDocument.Parse(schema);
        var root = source.RootElement;
        var properties = root.TryGetProperty("properties", out var configured)
            ? configured.Clone()
            : EmptyObject();
        var required = root.TryGetProperty("required", out var requiredElement)
            ? requiredElement.Clone()
            : EmptyArray();
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = required
        });
    }

    private static string ResolveSuccessCode(string operationId, JsonElement data, string fallback)
    {
        if (!operationId.Equals("commerce.prepare_checkout", StringComparison.OrdinalIgnoreCase)
            || data.ValueKind != JsonValueKind.Object)
            return fallback;
        if (data.TryGetProperty("payment_pending_manual_confirmation", out var manual)
            && manual.ValueKind == JsonValueKind.True)
            return "order.checkout_pending_manual_payment";
        if (data.TryGetProperty("payment_required", out var required)
            && required.ValueKind == JsonValueKind.True)
            return "order.checkout_payment_required";
        return "order.checkout_ready";
    }
    private static IReadOnlyList<string> ReadStrings(JsonElement root, string property) =>
        root.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList()
            : [];

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static JsonElement EmptyArray()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }
}
