using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

public interface IInboundMessageBatchProcessor
{
    Task<InboundMessageBatchProcessResult> ProcessAsync(
        Guid businessId,
        IReadOnlyList<IncomingMessage> messages,
        CancellationToken ct = default);
}

public sealed record InboundMessageBatchProcessResult(
    int MessageCount,
    int InteractiveMessageCount,
    bool SentToConversationProcessor);

public sealed class InboundMessageBatchProcessor : IInboundMessageBatchProcessor
{
    private readonly IWhatsAppMessageProcessorService _messageProcessor;
    private readonly ILogger<InboundMessageBatchProcessor> _logger;

    public InboundMessageBatchProcessor(
        IWhatsAppMessageProcessorService messageProcessor,
        ILogger<InboundMessageBatchProcessor> logger)
    {
        _messageProcessor = messageProcessor;
        _logger = logger;
    }

    public async Task<InboundMessageBatchProcessResult> ProcessAsync(
        Guid businessId,
        IReadOnlyList<IncomingMessage> messages,
        CancellationToken ct = default)
    {
        if (messages.Count == 0)
            return new InboundMessageBatchProcessResult(0, 0, false);

        var distinctUserNumbers = messages.Select(m => m.UserNumber).Distinct().ToList();
        if (distinctUserNumbers.Count > 1)
        {
            throw new InvalidOperationException(
                $"Se detectaron mensajes de multiples usuarios en un debounce: {string.Join(", ", distinctUserNumbers)}");
        }

        var userNumber = messages.First().UserNumber;
        var customerName = messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.CustomerName))?.CustomerName;
        var indexedMessages = messages
            .Select((message, index) => new IndexedIncomingMessage(
                index,
                message,
                HasInteractivePayload(message)))
            .ToList();

        var interactiveMessages = indexedMessages
            .Where(item => item.IsInteractive)
            .GroupBy(item => item.Message.InteractivePayload, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.Index)
            .ToList();

        foreach (var item in interactiveMessages)
        {
            ct.ThrowIfCancellationRequested();

            await ProcessSingleMessageAsync(
                businessId,
                userNumber,
                item.Message,
                customerName);
        }

        if (interactiveMessages.Count > 0)
        {
            _logger.LogInformation(
                "Debounce inbound: procesados primero {Count} mensaje(s) interactivo(s) para usuario {UserNumber} en negocio {BusinessId}",
                interactiveMessages.Count,
                userNumber,
                businessId);
        }

        var normalMessages = indexedMessages
            .Where(item => !item.IsInteractive)
            .Select(item => item.Message)
            .ToList();
        var combinedMessage = CombineMessageText(normalMessages);

        if (string.IsNullOrWhiteSpace(combinedMessage))
        {
            return new InboundMessageBatchProcessResult(
                messages.Count,
                interactiveMessages.Count,
                interactiveMessages.Count > 0);
        }

        ct.ThrowIfCancellationRequested();

        await _messageProcessor.ProcessIncomingMessageAsync(
            businessId,
            userNumber,
            combinedMessage,
            customerName,
            BuildMetadata(normalMessages));

        return new InboundMessageBatchProcessResult(messages.Count, interactiveMessages.Count, true);
    }

    private Task ProcessSingleMessageAsync(
        Guid businessId,
        string userNumber,
        IncomingMessage message,
        string? fallbackCustomerName) =>
        _messageProcessor.ProcessIncomingMessageAsync(
            businessId,
            userNumber,
            message.MessageText,
            string.IsNullOrWhiteSpace(message.CustomerName) ? fallbackCustomerName : message.CustomerName,
            BuildMetadata(message));

    private static bool HasInteractivePayload(IncomingMessage message) =>
        !string.IsNullOrWhiteSpace(message.InteractivePayload);

    private static string CombineMessageText(IEnumerable<IncomingMessage> messages) =>
        string.Join("\n", messages.Select(m => m.MessageText).Where(t => !string.IsNullOrWhiteSpace(t)));

    private static AgentInboundMetadata? BuildMetadata(IncomingMessage? message) =>
        message is null
            ? null
            : new AgentInboundMetadata(
                message.ProviderMessageId,
                message.ReplyToProviderMessageId,
                message.InteractivePayload,
                NormalizeFacts(message.Facts));

    private static AgentInboundMetadata? BuildMetadata(IReadOnlyList<IncomingMessage> messages)
    {
        var lastMessage = messages.LastOrDefault();
        if (lastMessage is null)
            return null;

        return new AgentInboundMetadata(
            lastMessage.ProviderMessageId,
            lastMessage.ReplyToProviderMessageId,
            lastMessage.InteractivePayload,
            MergeFacts(messages));
    }

    private static IReadOnlyDictionary<string, string> MergeFacts(IEnumerable<IncomingMessage> messages)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in messages)
        {
            foreach (var pair in NormalizeFacts(message.Facts))
                facts[pair.Key] = pair.Value;
        }

        return facts;
    }

    private static IReadOnlyDictionary<string, string> NormalizeFacts(
        IReadOnlyDictionary<string, string>? facts)
    {
        if (facts is null || facts.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return facts
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private sealed record IndexedIncomingMessage(
        int Index,
        IncomingMessage Message,
        bool IsInteractive);
}
