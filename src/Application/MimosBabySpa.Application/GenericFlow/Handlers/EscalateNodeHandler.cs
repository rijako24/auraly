using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.GenericFlow.Services;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Escalates the conversation to a human agent.
/// Sets Owner = Human, notifies via IEscalationNotifier.
///
/// Node config:
///   reason:           string   — escalation reason (supports templates)
///   escalationMessage: string  — message sent to the user (supports templates)
///   contacts:         string[] — WhatsApp numbers to notify (e.g. ["+573001234567"])
/// </summary>
public class EscalateNodeHandler : INodeHandler
{
    private readonly IEscalationNotifier _escalationNotifier;
    private readonly TemplateResolver _templateResolver;
    private readonly ILogger<EscalateNodeHandler> _logger;

    public FlowNodeType NodeType => FlowNodeType.Escalate;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.AdvancePast;

    public EscalateNodeHandler(
        IEscalationNotifier escalationNotifier,
        TemplateResolver templateResolver,
        ILogger<EscalateNodeHandler> logger)
    {
        _escalationNotifier = escalationNotifier;
        _templateResolver = templateResolver;
        _logger = logger;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node, FlowTurnContext ctx, CancellationToken ct)
    {
        ctx.State.Owner = ConversationOwner.Human;

        var reason = node.Config.TryGetProperty("reason", out var rp)
            ? _templateResolver.Resolve(rp.GetString() ?? string.Empty, ctx)
            : "Escalación automática";

        _logger.LogInformation("Escalating conversation for {Identifier}: {Reason}",
            ctx.UserIdentifier, reason);

        var escalationMsg = GetEscalationMessage(node.Config, ctx);
        var contacts = ReadContacts(node.Config);

        try
        {
            var notification = new EscalationNotification(
                ConversationId: ctx.ConversationId,
                CustomerPhone: ctx.UserIdentifier,
                Reason: reason,
                LastUserMessage: ctx.UserMessage
            );
            await _escalationNotifier.NotifyAsync(ctx.BusinessId, contacts, notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Escalation notification failed — conversation still transferred");
        }

        return NodeExecutionResult.WaitForUser(escalationMsg);
    }

    private string GetEscalationMessage(JsonElement config, FlowTurnContext ctx)
    {
        const string defaultMsg = "Te estoy conectando con un asesor. Un momento por favor.";

        if (config.TryGetProperty("escalationMessage", out var em) && em.GetString() is { } template)
            return _templateResolver.Resolve(template, ctx);

        return defaultMsg;
    }

    private static IReadOnlyList<string> ReadContacts(JsonElement config)
    {
        if (!config.TryGetProperty("contacts", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToList();
    }
}
