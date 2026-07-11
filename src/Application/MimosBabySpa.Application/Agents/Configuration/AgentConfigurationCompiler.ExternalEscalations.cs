namespace MimosBabySpa.Application.Agents.Configuration;

public sealed partial class AgentConfigurationCompiler
{
    private static void ValidateExternalEscalations(
        AgentConfig config,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        if (!config.Escalations.External.Enabled)
            return;

        foreach (var (eventName, definition) in config.Escalations.External.Events)
        {
            var path = $"escalations.external.events[{eventName}]";
            if (string.IsNullOrWhiteSpace(eventName))
                Error(errors, path, "event_name_required", "An external escalation event requires a non-empty name.");
            if (!definition.Enabled)
                continue;
            if (definition.AttemptTimeoutMinutes <= 0)
                Error(errors, path, "invalid_timeout", "attemptTimeoutMinutes must be positive.");
            if (string.IsNullOrWhiteSpace(definition.SendMessageSequence))
                Error(errors, path, "send_sequence_required", "An enabled external escalation requires sendMessageSequence.");
            else if (!config.MessageSequences.ContainsKey(definition.SendMessageSequence))
                Error(errors, path, "unknown_sequence", $"Message sequence '{definition.SendMessageSequence}' is not configured.");
            if (string.IsNullOrWhiteSpace(definition.ContactType) && definition.Contacts.Count == 0)
                Error(errors, path, "contact_route_required", "Configure contactType or at least one explicit contact.");

            foreach (var contact in definition.Contacts)
            {
                if (!contact.BusinessInboundContactId.HasValue)
                    Error(errors, $"{path}.contacts", "contact_id_required", "External escalation contacts must reference businessInboundContactId.");
            }

            foreach (var (outcomeKey, notificationEvent) in definition.OutcomeEvents)
            {
                var outcomePath = $"{path}.outcomeEvents[{outcomeKey}]";
                if (string.IsNullOrWhiteSpace(outcomeKey))
                    Error(errors, outcomePath, "outcome_key_required", "An outcome key is required.");
                if (string.IsNullOrWhiteSpace(notificationEvent))
                    Error(errors, outcomePath, "notification_event_required", "An outcome must reference a notification event.");
                else if (!config.Notifications.TryGetValue(notificationEvent, out var notification))
                    Error(errors, outcomePath, "unknown_notification", $"Notification event '{notificationEvent}' is not configured.");
                else if (!notification.Enabled)
                    Error(errors, outcomePath, "notification_disabled", $"Notification event '{notificationEvent}' must be enabled.");
            }
        }
    }
}
