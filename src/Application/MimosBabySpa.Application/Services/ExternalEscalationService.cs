using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class BusinessInboundContactRouter : IBusinessInboundContactRouter
{
    private readonly IUnitOfWork _unitOfWork;

    public BusinessInboundContactRouter(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<BusinessInboundContactRoute?> ResolveAsync(Guid businessId, string phone, CancellationToken ct = default)
    {
        var inboundContact = await _unitOfWork.BusinessInboundContacts.GetActiveByPhoneAsync(businessId, phone, ct);
        if (inboundContact is null)
            return null;

        return new BusinessInboundContactRoute(inboundContact.InboundAgentId, inboundContact.Key.Trim(), NormalizePhone(inboundContact.PhoneNumber));
    }

    internal static string NormalizePhone(string phone) =>
        new(phone.Where(char.IsDigit).ToArray());
}

public sealed class ExternalEscalationService : IExternalEscalationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AgentConfigProviderAccessor _configProvider;
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IWhatsAppService _whatsApp;
    private readonly ExternalEscalationOutcomePublisherAccessor _outcomes;

    public ExternalEscalationService(
        IUnitOfWork unitOfWork,
        AgentConfigProviderAccessor configProvider,
        IMessageSequenceResolver sequenceResolver,
        IWhatsAppService whatsApp,
        ExternalEscalationOutcomePublisherAccessor outcomes)
    {
        _unitOfWork = unitOfWork;
        _configProvider = configProvider;
        _sequenceResolver = sequenceResolver;
        _whatsApp = whatsApp;
        _outcomes = outcomes;
    }

    public async Task<ExternalEscalationSendResult> EscalateEventAsync(
        Guid sourceAgentId,
        string eventName,
        Guid targetId,
        IReadOnlyDictionary<string, string> custom,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return new ExternalEscalationSendResult(false, null, "event_name_required");

        return await EscalateAsync(
            new ExternalEscalationRequest(sourceAgentId, eventName.Trim(), targetId, custom),
            ct);
    }

    public async Task<ExternalEscalationSendResult> EscalateAsync(ExternalEscalationRequest request, CancellationToken ct = default)
    {
        var config = await _configProvider().GetConfigAsync(request.SourceAgentId, ct);
        if (!config.Escalations.External.Enabled
            || !config.Escalations.External.Events.TryGetValue(request.EventName, out var definition)
            || !definition.Enabled)
        {
            return new ExternalEscalationSendResult(false, null, "external_interaction_not_configured");
        }

        var previousAttempts = await _unitOfWork.ExternalEscalationAttempts.CountAttemptsAsync(
            config.BusinessId,
            request.EventName,
            request.EventName.Trim(),
            request.TargetId,
            ct);
        if (previousAttempts > 0)
            return new ExternalEscalationSendResult(false, null, "target_already_escalated");

        var contact = (await ResolveEscalationContactsAsync(config.BusinessId, definition, ct))
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.Key)
            .FirstOrDefault();

        if (contact is null)
            return new ExternalEscalationSendResult(false, null, "no_contact_available");

        var customPayload = BuildCustomPayload(request.Custom, definition, contact);
        var now = DateTime.UtcNow;
        var attempt = new ExternalEscalationAttempt
        {
            ExternalEscalationAttemptId = Guid.NewGuid(),
            BusinessId = config.BusinessId,
            SourceAgentId = config.AgentId,
            EventName = request.EventName,
            TargetType = request.EventName.Trim(),
            TargetId = request.TargetId,
            ContactKey = contact.Key.Trim(),
            ContactNameSnapshot = contact.Name.Trim(),
            ContactRoleSnapshot = contact.Role.Trim(),
            ContactPhoneSnapshot = BusinessInboundContactRouter.NormalizePhone(contact.Phone),
            InboundAgentIdSnapshot = contact.InboundAgentId,
            BusinessInboundContactIdSnapshot = contact.BusinessInboundContactId,
            ContactTypeSnapshot = string.IsNullOrWhiteSpace(contact.Type) ? null : contact.Type.Trim(),
            PickupAddressSnapshot = customPayload.TryGetValue("pickup_address", out var pickupAddress) && !string.IsNullOrWhiteSpace(pickupAddress) ? pickupAddress.Trim() : null,
            AttemptCode = BuildAttemptCode(definition, request.TargetId),
            CustomPayloadJson = JsonSerializer.Serialize(customPayload),
            Status = ExternalEscalationAttemptStatus.Pending,
            EscalatedAt = now,
            ExpiresAt = now.AddMinutes(Math.Max(1, definition.AttemptTimeoutMinutes))
        };

        await _unitOfWork.ExternalEscalationAttempts.AddAsync(attempt, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var sentMessageId = await SendEscalationMessagesAsync(config, definition, attempt, customPayload, ct);
        if (!string.IsNullOrWhiteSpace(sentMessageId))
        {
            attempt.WhatsAppMessageId = sentMessageId;
            await _unitOfWork.ExternalEscalationAttempts.UpdateAsync(attempt, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        if (definition.OutcomeEvents.ContainsKey(ExternalEscalationOutcomeKeys.Requested))
        {
            var deliveryId = await _outcomes().EnqueueAsync(
                config.BusinessId,
                attempt.ExternalEscalationAttemptId,
                ExternalEscalationOutcomeKeys.Requested,
                customPayload,
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _outcomes().PublishAsync(deliveryId, ct);
        }

        return new ExternalEscalationSendResult(true, attempt.AttemptCode, null, attempt.ExternalEscalationAttemptId);
    }

    public async Task<ExternalEscalationCompletionResult> CompleteAttemptAsync(
        ExternalEscalationCompletionRequest request,
        CancellationToken ct = default)
    {
        var attempt = await _unitOfWork.ExternalEscalationAttempts.GetByIdAsync(request.AttemptId, ct);
        if (attempt is null || attempt.BusinessId != request.BusinessId)
            return new ExternalEscalationCompletionResult(false, attempt, "La escalacion ya no esta disponible.");

        if (!string.IsNullOrWhiteSpace(request.ContactPhone)
            && !attempt.ContactPhoneSnapshot.Equals(BusinessInboundContactRouter.NormalizePhone(request.ContactPhone), StringComparison.OrdinalIgnoreCase))
        {
            return new ExternalEscalationCompletionResult(false, attempt, "La escalacion pertenece a otro contacto.");
        }

        if (attempt.Status != ExternalEscalationAttemptStatus.Pending || attempt.ExpiresAt <= DateTime.UtcNow)
            return new ExternalEscalationCompletionResult(false, attempt, "La escalacion ya no esta disponible.");

        var outcomeKey = request.OutcomeKey.Trim();
        if (string.IsNullOrWhiteSpace(outcomeKey))
            return new ExternalEscalationCompletionResult(false, attempt, "El resultado de la escalacion no esta configurado.");

        var now = DateTime.UtcNow;
        var payload = MergePayload(ReadCustomPayload(attempt.CustomPayloadJson), request.Payload);
        if (!string.IsNullOrWhiteSpace(request.ResponseText))
            payload["response_text"] = request.ResponseText.Trim();
        payload["outcome_key"] = outcomeKey;

        attempt.Status = request.CompletedStatus;
        attempt.CompletedAt = now;
        attempt.OutcomeKey = outcomeKey;
        attempt.ResponseText = string.IsNullOrWhiteSpace(request.ResponseText) ? null : request.ResponseText.Trim();
        attempt.ResponsePayloadJson = JsonSerializer.Serialize(payload);

        if (request.CompletedStatus == ExternalEscalationAttemptStatus.Accepted)
        {
            attempt.AcceptedAt = now;
            attempt.DeclinedAt = null;
        }
        else if (request.CompletedStatus == ExternalEscalationAttemptStatus.Declined)
        {
            attempt.DeclinedAt = now;
            attempt.AcceptedAt = null;
        }

        var deliveryId = await _outcomes().EnqueueAsync(
            request.BusinessId,
            attempt.ExternalEscalationAttemptId,
            outcomeKey,
            payload,
            ct);
        await _unitOfWork.ExternalEscalationAttempts.UpdateAsync(attempt, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await _outcomes().PublishAsync(deliveryId, ct);

        return new ExternalEscalationCompletionResult(true, attempt, "Escalacion completada.", outcomeKey, payload);
    }

    public async Task<IReadOnlyList<ExternalEscalationExpiredAttempt>> ProcessExpiredAttemptsAsync(CancellationToken ct = default)
    {
        var expired = await _unitOfWork.ExternalEscalationAttempts.GetExpiredPendingAttemptsAsync(DateTime.UtcNow, ct);
        var processed = new List<ExternalEscalationExpiredAttempt>();

        foreach (var attempt in expired)
        {
            var timedOutOutcome = ExternalEscalationOutcomeKeys.TimedOut;

            var now = DateTime.UtcNow;
            var payload = new Dictionary<string, string>(ReadCustomPayload(attempt.CustomPayloadJson), StringComparer.OrdinalIgnoreCase)
            {
                ["outcome_key"] = timedOutOutcome
            };

            attempt.Status = ExternalEscalationAttemptStatus.TimedOut;
            attempt.TimedOutAt = now;
            attempt.CompletedAt = now;
            attempt.OutcomeKey = timedOutOutcome;
            attempt.ResponsePayloadJson = JsonSerializer.Serialize(payload);
            await _outcomes().EnqueueAsync(
                attempt.BusinessId,
                attempt.ExternalEscalationAttemptId,
                timedOutOutcome,
                payload,
                ct);
            await _unitOfWork.ExternalEscalationAttempts.UpdateAsync(attempt, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            processed.Add(new ExternalEscalationExpiredAttempt(
                attempt.BusinessId,
                attempt.ExternalEscalationAttemptId,
                attempt.EventName,
                attempt.TargetType,
                attempt.TargetId,
                timedOutOutcome,
                payload));
        }

        return processed;
    }

    private async Task<IReadOnlyList<ResolvedEscalationContact>> ResolveEscalationContactsAsync(
        Guid businessId,
        ExternalEscalationEventDefinition definition,
        CancellationToken ct)
    {
        var contacts = new List<ResolvedEscalationContact>();
        var contactType = definition.ContactType?.Trim() ?? string.Empty;

        foreach (var configured in definition.Contacts.OrderBy(c => c.Priority))
        {
            if (configured.BusinessInboundContactId is not Guid businessInboundContactId)
                continue;

            var inboundContact = await _unitOfWork.BusinessInboundContacts.GetByIdAsync(businessInboundContactId, ct);
            if (inboundContact is null
                || !inboundContact.IsActive
                || inboundContact.BusinessId != businessId
                || inboundContact.InboundAgent is null
                || !inboundContact.InboundAgent.IsActive
                || inboundContact.InboundAgent.BusinessId != businessId)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(contactType)
                && !inboundContact.Type.Equals(contactType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            contacts.Add(ToResolvedContact(inboundContact, configured.Priority, configured.PickupAddress));
        }

        if (contacts.Count == 0 && !string.IsNullOrWhiteSpace(contactType))
        {
            var inboundContacts = await _unitOfWork.BusinessInboundContacts.GetActiveByBusinessAndTypeAsync(businessId, contactType, ct);
            contacts.AddRange(inboundContacts
                .OrderBy(c => c.Key)
                .Select((contact, index) => ToResolvedContact(contact, index + 1, null)));
        }

        return contacts;
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
            ["external_interaction_id"] = attempt.ExternalEscalationAttemptId.ToString(),
            ["attempt_code"] = attempt.AttemptCode,
            ["contact_name"] = attempt.ContactNameSnapshot,
            ["contact_role"] = attempt.ContactRoleSnapshot,
            ["contact_type"] = attempt.ContactTypeSnapshot ?? string.Empty,
            ["business_inbound_contact_id"] = attempt.BusinessInboundContactIdSnapshot?.ToString() ?? string.Empty,
            ["pickup_address"] = attempt.PickupAddressSnapshot ?? string.Empty
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
                    message.Template.HeaderParameters,
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

    private static ResolvedEscalationContact ToResolvedContact(
        BusinessInboundContact contact,
        int priority,
        string? pickupAddress) =>
        new(
            contact.BusinessInboundContactId,
            contact.Type,
            contact.Key,
            contact.Name,
            string.IsNullOrWhiteSpace(contact.Role) ? contact.Type : contact.Role,
            contact.PhoneNumber,
            contact.InboundAgentId,
            priority,
            pickupAddress);

    private sealed record ResolvedEscalationContact(
        Guid? BusinessInboundContactId,
        string Type,
        string Key,
        string Name,
        string Role,
        string Phone,
        Guid InboundAgentId,
        int Priority,
        string? PickupAddress);

    private static IReadOnlyDictionary<string, string> BuildCustomPayload(
        IReadOnlyDictionary<string, string> custom,
        ExternalEscalationEventDefinition definition,
        ResolvedEscalationContact contact)
    {
        var payload = new Dictionary<string, string>(custom, StringComparer.OrdinalIgnoreCase);
        var pickupAddress = !string.IsNullOrWhiteSpace(definition.PickupAddress)
            ? definition.PickupAddress
            : contact.PickupAddress;

        if (!string.IsNullOrWhiteSpace(pickupAddress))
            payload["pickup_address"] = pickupAddress.Trim();

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
