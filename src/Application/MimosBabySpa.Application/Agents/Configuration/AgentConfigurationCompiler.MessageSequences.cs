namespace MimosBabySpa.Application.Agents.Configuration;

public sealed partial class AgentConfigurationCompiler
{
    private static readonly HashSet<string> SupportedMessageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text",
        "whatsapp_template"
    };

    private static void ValidateMessageSequences(
        AgentConfig config,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        foreach (var (sequenceName, sequence) in config.MessageSequences)
        {
            var sequencePath = $"messageSequences[{sequenceName}]";
            if (string.IsNullOrWhiteSpace(sequenceName))
            {
                Error(errors, "messageSequences", "sequence_name_required", "Message sequence names cannot be empty.");
                continue;
            }
            if (sequence.Messages.Count == 0)
                Error(errors, sequencePath, "sequence_messages_required", "A message sequence requires at least one message.");

            for (var index = 0; index < sequence.Messages.Count; index++)
            {
                var step = sequence.Messages[index];
                var path = $"{sequencePath}.messages[{index}]";
                if (!SupportedMessageTypes.Contains(step.Type))
                    Error(errors, path, "unsupported_message_type", $"Message type '{step.Type}' is not supported.");

                var isTemplate = step.Type.Equals("whatsapp_template", StringComparison.OrdinalIgnoreCase);
                if (isTemplate && string.IsNullOrWhiteSpace(step.TemplateName))
                    Error(errors, path, "template_name_required", "WhatsApp template messages require templateName.");
                if (!isTemplate
                    && string.IsNullOrWhiteSpace(step.Body)
                    && !step.AttachmentId.HasValue)
                {
                    Error(errors, path, "message_content_required", "Text messages require body or attachmentId.");
                }

                ValidateButtons(step, path, errors);
            }
        }

        foreach (var (eventName, notification) in config.Notifications)
        {
            var path = $"notifications[{eventName}]";
            if (!notification.Enabled)
                continue;
            if (string.IsNullOrWhiteSpace(notification.SendMessageSequence))
                Error(errors, path, "notification_sequence_required", "Enabled notifications require sendMessageSequence.");
            else if (!config.MessageSequences.ContainsKey(notification.SendMessageSequence))
                Error(errors, path, "unknown_notification_sequence", $"Message sequence '{notification.SendMessageSequence}' is not configured.");
            if (notification.Recipients.Count == 0)
                Error(errors, path, "notification_recipients_required", "Enabled notifications require at least one recipient.");
        }
    }

    private static void ValidateButtons(
        MessageSequenceStep step,
        string path,
        ICollection<AgentConfigurationDiagnostic> errors)
    {
        if (step.Buttons.Count > 3)
            Error(errors, path, "too_many_buttons", "WhatsApp supports at most three reply buttons per message.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < step.Buttons.Count; index++)
        {
            var button = step.Buttons[index];
            var buttonPath = $"{path}.buttons[{index}]";
            var id = button.Id?.Trim() ?? string.Empty;
            var title = button.Title?.Trim() ?? string.Empty;
            if (id.Length == 0)
                Error(errors, buttonPath, "button_id_required", "Reply buttons require a non-empty id.");
            else
            {
                if (id.Length > 256)
                    Error(errors, buttonPath, "button_id_too_long", "Reply button ids cannot exceed 256 characters.");
                if (!ids.Add(id))
                    Error(errors, buttonPath, "duplicate_button_id", $"Reply button id '{id}' is duplicated in the same message.");
            }

            if (title.Length == 0)
                Error(errors, buttonPath, "button_title_required", "Reply buttons require a non-empty title.");
            else if (title.Length > 20)
                Error(errors, buttonPath, "button_title_too_long", "Reply button titles cannot exceed 20 characters.");
        }

        if (step.Buttons.Count > 0
            && !step.Type.Equals("whatsapp_template", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(step.Body))
        {
            Error(errors, path, "button_body_required", "Interactive button messages require a non-empty body.");
        }
    }
}
