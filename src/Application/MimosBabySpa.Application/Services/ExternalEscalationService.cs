using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class ExternalEscalationRouter : IExternalEscalationRouter
{
    private readonly IAgentRepository _agents;
    private readonly IAgentConfigProvider _configProvider;

    public ExternalEscalationRouter(IAgentRepository agents, IAgentConfigProvider configProvider)
    {
        _agents = agents;
        _configProvider = configProvider;
    }

    public async Task<ExternalEscalationRoute?> ResolveAsync(Guid businessId, string phone, CancellationToken ct = default)
    {
        var match = await FindContactAsync(_agents, _configProvider, businessId, phone, ct);
        if (match?.Contact.InboundAgentId is not Guid inboundAgentId)
            return null;

        return new ExternalEscalationRoute(inboundAgentId, match.Contact.Key.Trim(), NormalizePhone(match.Contact.Phone));
    }

    internal static async Task<ExternalEscalationContactMatch?> FindContactAsync(
        IAgentRepository agents,
        IAgentConfigProvider configProvider,
        Guid businessId,
        string phone,
        CancellationToken ct)
    {
        var normalized = NormalizePhone(phone);
        var businessAgents = await agents.GetByBusinessAsync(businessId, ct);

        foreach (var agent in businessAgents.Where(a => a.IsActive))
        {
            var config = await configProvider.GetConfigAsync(agent.AgentId, ct);
            if (!config.Escalations.External.Enabled)
                continue;

            foreach (var (eventName, definition) in config.Escalations.External.Events)
            {
                if (!definition.Enabled)
                    continue;

                var contact = definition.Contacts.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.Phone)
                    && NormalizePhone(c.Phone).Equals(normalized, StringComparison.OrdinalIgnoreCase));

                if (contact is not null)
                    return new ExternalEscalationContactMatch(agent.AgentId, businessId, eventName, contact);
            }
        }

        return null;
    }

    internal static string NormalizePhone(string phone) =>
        new(phone.Where(char.IsDigit).ToArray());
}

public sealed class ExternalEscalationService : IExternalEscalationService
{
    private static readonly Regex AttemptCodeRegex = new(@"\b[A-Z]{2,10}-\d{4,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentConfigProvider _configProvider;
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IWhatsAppService _whatsApp;
    private readonly IEventNotificationDispatcher _notificationDispatcher;
    private readonly IEnumerable<IExternalEscalationTargetHandler> _targetHandlers;
    private readonly ILogger<ExternalEscalationService> _logger;

    public ExternalEscalationService(
        IUnitOfWork unitOfWork,
        IAgentConfigProvider configProvider,
        IMessageSequenceResolver sequenceResolver,
        IWhatsAppService whatsApp,
        IEventNotificationDispatcher notificationDispatcher,
        IEnumerable<IExternalEscalationTargetHandler> targetHandlers,
        ILogger<ExternalEscalationService> logger)
    {
        _unitOfWork = unitOfWork;
        _configProvider = configProvider;
        _sequenceResolver = sequenceResolver;
        _whatsApp = whatsApp;
        _notificationDispatcher = notificationDispatcher;
        _targetHandlers = targetHandlers;
        _logger = logger;
    }

    public async Task<ExternalEscalationSendResult> EscalateNextAsync(ExternalEscalationRequest request, CancellationToken ct = default)
    {
        var config = await _configProvider.GetConfigAsync(request.SourceAgentId, ct);
        if (!config.Escalations.External.Enabled
            || !config.Escalations.External.Events.TryGetValue(request.EventName, out var definition)
            || !definition.Enabled)
        {
            return new ExternalEscalationSendResult(false, null, "external_interaction_not_configured");
        }

        if (await _unitOfWork.ExternalEscalationAttempts.HasAcceptedForTargetAsync(
                config.BusinessId,
                request.EventName,
                request.TargetType,
                request.TargetId,
                ct))
        {
            return new ExternalEscalationSendResult(false, null, "target_already_completed");
        }

        var attempts = await _unitOfWork.ExternalEscalationAttempts.CountAttemptsAsync(
            config.BusinessId,
            request.EventName,
            request.TargetType,
            request.TargetId,
            ct);

        var contact = definition.Contacts
            .Where(c => c.InboundAgentId.HasValue && !string.IsNullOrWhiteSpace(c.Phone) && !string.IsNullOrWhiteSpace(c.Key))
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.Key)
            .Skip(attempts)
            .FirstOrDefault();

        if (contact is null)
            return new ExternalEscalationSendResult(false, null, "no_more_contacts");

        var customPayload = BuildCustomPayload(request.Custom, contact);
        var now = DateTime.UtcNow;
        var attempt = new ExternalEscalationAttempt
        {
            ExternalEscalationAttemptId = Guid.NewGuid(),
            BusinessId = config.BusinessId,
            SourceAgentId = config.AgentId,
            EventName = request.EventName,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            ContactKey = contact.Key.Trim(),
            ContactNameSnapshot = contact.Name.Trim(),
            ContactRoleSnapshot = contact.Role.Trim(),
            ContactPhoneSnapshot = ExternalEscalationRouter.NormalizePhone(contact.Phone),
            InboundAgentIdSnapshot = contact.InboundAgentId!.Value,
            AttemptCode = BuildAttemptCode(definition, request.TargetId),
            CustomPayloadJson = JsonSerializer.Serialize(customPayload),
            Status = ExternalEscalationAttemptStatus.Pending,
            EscalatedAt = now,
            ExpiresAt = now.AddMinutes(Math.Max(1, definition.AttemptTimeoutMinutes))
        };

        await _unitOfWork.ExternalEscalationAttempts.AddAsync(attempt, ct);
        await DispatchSentAsync(attempt, customPayload, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var sentMessageId = await SendEscalationMessagesAsync(config, definition, attempt, customPayload, ct);
        if (!string.IsNullOrWhiteSpace(sentMessageId))
        {
            attempt.WhatsAppMessageId = sentMessageId;
            await _unitOfWork.ExternalEscalationAttempts.UpdateAsync(attempt, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        await TrySendNotificationEventAsync(
            config,
            definition.AttemptSentNotificationEvent,
            attempt,
            customPayload,
            ct);

        return new ExternalEscalationSendResult(true, attempt.AttemptCode, null, attempt.ExternalEscalationAttemptId);
    }

    public async Task<ExternalEscalationResolution> ResolveAttemptAsync(
        Guid businessId,
        string contactPhone,
        string messageText,
        string? interactivePayload,
        string? replyToProviderMessageId,
        CancellationToken ct = default)
    {
        var byPayload = TryParseEscalationPayload(interactivePayload, out var payloadAttemptId, out var payloadAction)
            ? await _unitOfWork.ExternalEscalationAttempts.GetByIdAsync(payloadAttemptId, ct)
            : null;

        if (IsUsableAttempt(byPayload, businessId, contactPhone))
            return new ExternalEscalationResolution("resolved", byPayload, [], null, payloadAction);

        if (!string.IsNullOrWhiteSpace(replyToProviderMessageId))
        {
            var byReply = await _unitOfWork.ExternalEscalationAttempts.GetByWhatsAppMessageIdAsync(
                businessId,
                replyToProviderMessageId,
                contactPhone,
                ct);
            if (byReply is not null)
                return new ExternalEscalationResolution("resolved", byReply, [], null, null);
        }

        var code = TryExtractAttemptCode(messageText);
        if (!string.IsNullOrWhiteSpace(code))
        {
            var byCode = await _unitOfWork.ExternalEscalationAttempts.GetByAttemptCodeAsync(businessId, code, contactPhone, ct);
            if (byCode is not null)
                return new ExternalEscalationResolution("resolved", byCode, [], null, null);
        }

        var open = await _unitOfWork.ExternalEscalationAttempts.GetPendingByContactPhoneAsync(businessId, contactPhone, ct);
        if (open.Count == 1)
            return new ExternalEscalationResolution("resolved", open[0], open, null, null);

        return new ExternalEscalationResolution(
            open.Count == 0 ? "not_found" : "ambiguous",
            null,
            open,
            open.Count == 0 ? "No pending external interactions were found for this contact." : "Multiple pending external interactions require a code or quoted message.");
    }

    public async Task<ExternalEscalationActionResult> CompleteAsync(
        Guid businessId,
        Guid attemptId,
        string contactPhone,
        string outcomeKey,
        string? responseText,
        IReadOnlyDictionary<string, string>? responsePayload = null,
        CancellationToken ct = default)
    {
        var attempt = await _unitOfWork.ExternalEscalationAttempts.GetByIdAsync(attemptId, ct);
        if (!IsUsableAttempt(attempt, businessId, contactPhone))
            return new ExternalEscalationActionResult(false, attempt, "La interaccion ya no esta disponible.", false);

        var normalizedOutcome = NormalizeOutcome(outcomeKey);
        if (IsDeclineOutcome(normalizedOutcome))
            return await DeclineAsync(businessId, attemptId, contactPhone, responseText, responsePayload, ct);

        if (await _unitOfWork.ExternalEscalationAttempts.HasAcceptedForTargetAsync(
                attempt!.BusinessId,
                attempt.EventName,
                attempt.TargetType,
                attempt.TargetId,
                ct))
        {
            attempt.Status = ExternalEscalationAttemptStatus.Cancelled;
            attempt.CancelledAt = DateTime.UtcNow;
            await _unitOfWork.ExternalEscalationAttempts.UpdateAsync(attempt, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return new ExternalEscalationActionResult(false, attempt, "La interaccion ya fue completada por otro contacto.", false);
        }

        var now = DateTime.UtcNow;
        var payload = MergePayload(ReadCustomPayload(attempt.CustomPayloadJson), responsePayload);
        if (!string.IsNullOrWhiteSpace(responseText))
            payload["response_text"] = responseText.Trim();
        payload["outcome_key"] = normalizedOutcome;

        attempt.Status = ExternalEscalationAttemptStatus.Accepted;
        attempt.AcceptedAt = now;
        attempt.CompletedAt = now;
        attempt.OutcomeKey = normalizedOutcome;
        attempt.ResponseText = string.IsNullOrWhiteSpace(responseText) ? null : responseText.Trim();
        attempt.ResponsePayloadJson = JsonSerializer.Serialize(payload);
        await _unitOfWork.ExternalEscalationAttempts.UpdateAsync(attempt, ct);

        var completion = new ExternalEscalationCompletion(normalizedOutcome, responseText, payload);
        await DispatchCompletedAsync(attempt, completion, ct);
        await _unitOfWork.ExternalEscalationAttempts.CancelPendingForTargetAsync(
            attempt.BusinessId,
            attempt.EventName,
            attempt.TargetType,
            attempt.TargetId,
            attempt.ExternalEscalationAttemptId,
            ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var config = await _configProvider.GetConfigAsync(attempt.SourceAgentId, ct);
        var notificationEvent = config.Escalations.External.Events.TryGetValue(attempt.EventName, out var definition)
            ? definition.CompletedNotificationEvent ?? definition.AcceptedNotificationEvent
            : null;

        await TrySendNotificationEventAsync(config, notificationEvent, attempt, payload, ct);

        return new ExternalEscalationActionResult(
            true,
            attempt,
            "Interaccion completada.",
            false,
            normalizedOutcome,
            responseText,
            payload);
    }

    public Task<ExternalEscalationActionResult> AcceptAsync(Guid businessId, Guid attemptId, string contactPhone, CancellationToken ct = default) =>
        CompleteAsync(businessId, attemptId, contactPhone, "accepted", null, null, ct);

    public Task<ExternalEscalationActionResult> DeclineAsync(Guid businessId, Guid attemptId, string contactPhone, CancellationToken ct = default) =>
        DeclineAsync(businessId, attemptId, contactPhone, null, null, ct);

    private async Task<ExternalEscalationActionResult> DeclineAsync(
        Guid businessId,
        Guid attemptId,
        string contactPhone,
        string? responseText,
        IReadOnlyDictionary<string, string>? responsePayload,
        CancellationToken ct)
    {
        var attempt = await _unitOfWork.ExternalEscalationAttempts.GetByIdAsync(attemptId, ct);
        if (!IsUsableAttempt(attempt, businessId, contactPhone))
            return new ExternalEscalationActionResult(false, attempt, "La interaccion ya no esta disponible.", false);

        var now = DateTime.UtcNow;
        var basePayload = ReadCustomPayload(attempt!.CustomPayloadJson);
        var completionPayload = MergePayload(basePayload, responsePayload);
        if (!string.IsNullOrWhiteSpace(responseText))
            completionPayload["response_text"] = responseText.Trim();
        completionPayload["outcome_key"] = "declined";

        attempt.Status = ExternalEscalationAttemptStatus.Declined;
        attempt.DeclinedAt = now;
        attempt.CompletedAt = now;
        attempt.OutcomeKey = "declined";
        attempt.ResponseText = string.IsNullOrWhiteSpace(responseText) ? null : responseText.Trim();
        attempt.ResponsePayloadJson = JsonSerializer.Serialize(completionPayload);
        await _unitOfWork.ExternalEscalationAttempts.UpdateAsync(attempt, ct);

        await DispatchDeclinedAsync(attempt, completionPayload, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var next = await EscalateNextAsync(
            new ExternalEscalationRequest(
                attempt.SourceAgentId,
                attempt.EventName,
                attempt.TargetType,
                attempt.TargetId,
                basePayload),
            ct);

        if (!next.Sent && string.Equals(next.Error, "no_more_contacts", StringComparison.OrdinalIgnoreCase))
            await NotifyContactsExhaustedAsync(attempt, ct);

        return new ExternalEscalationActionResult(
            true,
            attempt,
            next.Sent ? "Interaccion rechazada; se envio al siguiente contacto." : "Interaccion rechazada.",
            next.Sent,
            "declined",
            responseText,
            completionPayload);
    }

    public async Task ProcessExpiredAttemptsAsync(CancellationToken ct = default)
    {
        var expired = await _unitOfWork.ExternalEscalationAttempts.GetExpiredPendingAttemptsAsync(DateTime.UtcNow, ct);
        foreach (var attempt in expired)
        {
            var now = DateTime.UtcNow;
            attempt.Status = ExternalEscalationAttemptStatus.TimedOut;
            attempt.TimedOutAt = now;
            await _unitOfWork.ExternalEscalationAttempts.UpdateAsync(attempt, ct);

            var payload = ReadCustomPayload(attempt.CustomPayloadJson);
            await DispatchTimedOutAsync(attempt, payload, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            var next = await EscalateNextAsync(
                new ExternalEscalationRequest(
                    attempt.SourceAgentId,
                    attempt.EventName,
                    attempt.TargetType,
                    attempt.TargetId,
                    payload),
                ct);

            if (!next.Sent && string.Equals(next.Error, "no_more_contacts", StringComparison.OrdinalIgnoreCase))
                await NotifyContactsExhaustedAsync(attempt, ct);
        }
    }

    private async Task<string?> SendEscalationMessagesAsync(
        AgentConfig config,
        ExternalEscalationEventDefinition definition,
        ExternalEscalationAttempt attempt,
        IReadOnlyDictionary<string, string> custom,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(definition.SendMessageSequence))
            return null;

        var business = await _unitOfWork.Businesses.GetByIdAsync(config.BusinessId);
        var context = new Dictionary<string, string>(custom, StringComparer.OrdinalIgnoreCase)
        {
            ["business_name"] = business?.Name ?? string.Empty,
            ["pickup_contact_name"] = business?.Name ?? string.Empty,
            ["external_escalation_id"] = attempt.ExternalEscalationAttemptId.ToString(),
            ["external_interaction_id"] = attempt.ExternalEscalationAttemptId.ToString(),
            ["attempt_code"] = attempt.AttemptCode,
            ["contact_name"] = attempt.ContactNameSnapshot,
            ["contact_role"] = attempt.ContactRoleSnapshot
        };

        var messages = (await _sequenceResolver.ResolveAsync(
            config.BusinessId,
            definition.SendMessageSequence,
            config.MessageSequences,
            new MessageSequenceContext { Custom = context },
            ct)).ToList();

        if (messages.Count == 0)
            return null;


        string? sentMessageId = null;
        foreach (var message in messages)
        {
            if (message.Template is not null)
            {
                sentMessageId ??= await _whatsApp.SendTemplateMessageAsync(
                    config.BusinessId,
                    attempt.ContactPhoneSnapshot,
                    message.Template.Name,
                    message.Template.LanguageCode,
                    message.Template.BodyParameters,
                    message.Buttons);
                continue;
            }

            if (message.Buttons is { Count: > 0 } buttons)
            {
                sentMessageId ??= await _whatsApp.SendButtonMessageAsync(
                    config.BusinessId,
                    attempt.ContactPhoneSnapshot,
                    message.Body ?? string.Empty,
                    buttons);
                continue;
            }

            if (string.IsNullOrWhiteSpace(message.MediaUrl))
            {
                await _whatsApp.SendTextMessageAsync(config.BusinessId, attempt.ContactPhoneSnapshot, message.Body ?? string.Empty);
                continue;
            }

            if (string.Equals(message.MediaType, "image", StringComparison.OrdinalIgnoreCase))
                await _whatsApp.SendImageMessageAsync(config.BusinessId, attempt.ContactPhoneSnapshot, message.MediaUrl, message.Body);
            else
                await _whatsApp.SendDocumentMessageAsync(config.BusinessId, attempt.ContactPhoneSnapshot, message.MediaUrl, message.Body, message.Filename);
        }

        return sentMessageId;
    }


    private async Task NotifyContactsExhaustedAsync(ExternalEscalationAttempt attempt, CancellationToken ct)
    {
        var config = await _configProvider.GetConfigAsync(attempt.SourceAgentId, ct);
        var notificationEvent = config.Escalations.External.Events.TryGetValue(attempt.EventName, out var definition)
            ? definition.ExhaustedNotificationEvent
            : null;

        var payload = ReadCustomPayload(attempt.CustomPayloadJson);
        await DispatchExhaustedAsync(attempt, payload, ct);
        await TrySendNotificationEventAsync(config, notificationEvent, attempt, payload, ct);
    }

    private async Task TrySendNotificationEventAsync(
        AgentConfig config,
        string? notificationEvent,
        ExternalEscalationAttempt attempt,
        IReadOnlyDictionary<string, string> custom,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(notificationEvent))
            return;

        try
        {
            await _notificationDispatcher.SendEventAsync(
                config.BusinessId,
                config,
                notificationEvent.Trim(),
                BuildNotificationPayload(attempt, custom),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "External interaction notification '{Event}' failed for AttemptId={AttemptId} BusinessId={BusinessId}",
                notificationEvent,
                attempt.ExternalEscalationAttemptId,
                config.BusinessId);
        }
    }

    private async Task DispatchSentAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct)
    {
        foreach (var handler in ResolveHandlers(attempt))
            await handler.OnAttemptSentAsync(attempt, payload, ct);
    }

    private async Task DispatchCompletedAsync(ExternalEscalationAttempt attempt, ExternalEscalationCompletion completion, CancellationToken ct)
    {
        foreach (var handler in ResolveHandlers(attempt))
            await handler.OnAttemptCompletedAsync(attempt, completion, ct);
    }

    private async Task DispatchDeclinedAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct)
    {
        foreach (var handler in ResolveHandlers(attempt))
            await handler.OnAttemptDeclinedAsync(attempt, payload, ct);
    }

    private async Task DispatchTimedOutAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct)
    {
        foreach (var handler in ResolveHandlers(attempt))
            await handler.OnAttemptTimedOutAsync(attempt, payload, ct);
    }

    private async Task DispatchExhaustedAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct)
    {
        foreach (var handler in ResolveHandlers(attempt))
            await handler.OnAttemptsExhaustedAsync(attempt, payload, ct);
    }

    private IEnumerable<IExternalEscalationTargetHandler> ResolveHandlers(ExternalEscalationAttempt attempt) =>
        _targetHandlers.Where(handler => handler.CanHandle(attempt.EventName, attempt.TargetType));

    private static Dictionary<string, string> BuildNotificationPayload(
        ExternalEscalationAttempt attempt,
        IReadOnlyDictionary<string, string> custom)
    {
        var payload = new Dictionary<string, string>(custom, StringComparer.OrdinalIgnoreCase)
        {
            ["external_escalation_id"] = attempt.ExternalEscalationAttemptId.ToString(),
            ["external_interaction_id"] = attempt.ExternalEscalationAttemptId.ToString(),
            ["external_event_name"] = attempt.EventName,
            ["target_type"] = attempt.TargetType,
            ["target_id"] = attempt.TargetId.ToString(),
            ["attempt_code"] = attempt.AttemptCode,
            ["contact_key"] = attempt.ContactKey,
            ["contact_name"] = attempt.ContactNameSnapshot,
            ["contact_role"] = attempt.ContactRoleSnapshot,
            ["contact_phone"] = attempt.ContactPhoneSnapshot,
            ["escalated_at_utc"] = attempt.EscalatedAt.ToString("O")
        };

        if (attempt.AcceptedAt is DateTime acceptedAt)
            payload["completed_at_utc"] = acceptedAt.ToString("O");

        return payload;
    }

    private static IReadOnlyDictionary<string, string> BuildCustomPayload(
        IReadOnlyDictionary<string, string> custom,
        ExternalEscalationContactDefinition contact)
    {
        var payload = new Dictionary<string, string>(custom, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(contact.PickupAddress))
            payload["pickup_address"] = contact.PickupAddress.Trim();

        return payload;
    }

    private static string BuildAttemptCode(ExternalEscalationEventDefinition definition, Guid targetId)
    {
        var prefix = string.IsNullOrWhiteSpace(definition.AttemptCodePrefix)
            ? "EXT"
            : Regex.Replace(definition.AttemptCodePrefix.Trim().ToUpperInvariant(), "[^A-Z0-9]", string.Empty);

        var number = Math.Abs(targetId.GetHashCode()) % 100000;
        return $"{prefix}-{number:00000}";
    }

    internal static bool TryParseEscalationPayload(string? payload, out Guid attemptId, out string? action)
    {
        attemptId = Guid.Empty;
        action = null;

        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || (!parts[0].Equals("external_interaction", StringComparison.OrdinalIgnoreCase)
                && !parts[0].Equals("external_escalation", StringComparison.OrdinalIgnoreCase)))
            return false;

        action = NormalizeOutcome(parts[1]);
        return Guid.TryParseExact(parts[2], "N", out attemptId)
            || Guid.TryParse(parts[2], out attemptId);
    }

    private static string? TryExtractAttemptCode(string messageText)
    {
        var match = AttemptCodeRegex.Match(messageText ?? string.Empty);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static bool IsUsableAttempt(ExternalEscalationAttempt? attempt, Guid businessId, string contactPhone) =>
        attempt is not null
        && attempt.BusinessId == businessId
        && attempt.Status == ExternalEscalationAttemptStatus.Pending
        && attempt.ExpiresAt > DateTime.UtcNow
        && attempt.ContactPhoneSnapshot.Equals(ExternalEscalationRouter.NormalizePhone(contactPhone), StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> MergePayload(
        IReadOnlyDictionary<string, string> basePayload,
        IReadOnlyDictionary<string, string>? responsePayload)
    {
        var merged = new Dictionary<string, string>(basePayload, StringComparer.OrdinalIgnoreCase);
        if (responsePayload is null)
            return merged;

        foreach (var (key, value) in responsePayload)
        {
            if (!string.IsNullOrWhiteSpace(key))
                merged[key] = value ?? string.Empty;
        }

        return merged;
    }

    private static bool IsDeclineOutcome(string outcomeKey) =>
        outcomeKey.Equals("declined", StringComparison.OrdinalIgnoreCase)
        || outcomeKey.Equals("rejected", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOutcome(string? outcomeKey)
    {
        if (string.IsNullOrWhiteSpace(outcomeKey))
            return "completed";

        var value = outcomeKey.Trim().ToLowerInvariant();
        return value switch
        {
            "accept" => "accepted",
            "accepted" => "accepted",
            "decline" => "declined",
            "declined" => "declined",
            _ => value
        };
    }

    private static IReadOnlyDictionary<string, string> ReadCustomPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

