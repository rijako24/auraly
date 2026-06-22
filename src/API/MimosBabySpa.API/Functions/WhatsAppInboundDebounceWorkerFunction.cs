using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.API.Services;

namespace MimosBabySpa.API.Functions;

public sealed class WhatsAppInboundDebounceWorkerFunction
{
    private const string WhatsAppProvider = "whatsapp";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IInboundMessageDeduplicationService _deduplicationService;
    private readonly IWhatsAppInboundQueueService _queueService;
    private readonly IWhatsAppWebhookParserService _webhookParserService;
    private readonly IWhatsAppMessageProcessorService _messageProcessorService;
    private readonly ILogger<WhatsAppInboundDebounceWorkerFunction> _logger;

    public WhatsAppInboundDebounceWorkerFunction(
        IInboundMessageDeduplicationService deduplicationService,
        IWhatsAppInboundQueueService queueService,
        IWhatsAppWebhookParserService webhookParserService,
        IWhatsAppMessageProcessorService messageProcessorService,
        ILogger<WhatsAppInboundDebounceWorkerFunction> logger)
    {
        _deduplicationService = deduplicationService;
        _queueService = queueService;
        _webhookParserService = webhookParserService;
        _messageProcessorService = messageProcessorService;
        _logger = logger;
    }

    [Function("WhatsAppInboundDebounceWorker")]
    public async Task Run(
        [ServiceBusTrigger(WhatsAppInboundQueueService.DefaultQueueName, Connection = "ServiceBusConnection", IsSessionsEnabled = true)] string body,
        CancellationToken ct)
    {
        var wakeup = JsonSerializer.Deserialize<WhatsAppInboundDebounceMessage>(body, JsonOptions)
            ?? throw new InvalidOperationException("Mensaje de debounce inbound inválido.");

        var pending = (await _deduplicationService.GetPendingConversationMessagesAsync(
            wakeup.BusinessId,
            wakeup.Provider,
            wakeup.UserNumber,
            ct)).ToList();

        if (pending.Count == 0)
        {
            _logger.LogDebug(
                "No hay mensajes inbound pendientes para BusinessId={BusinessId}, UserNumber={UserNumber}",
                wakeup.BusinessId,
                wakeup.UserNumber);
            return;
        }

        var latest = pending.MaxBy(r => r.ReceivedAtUtc)!;
        var nextDueAtUtc = latest.ReceivedAtUtc.Add(DebounceDelay);
        var now = DateTime.UtcNow;

        if (now < nextDueAtUtc)
        {
            await _queueService.ScheduleDebounceAsync(
                wakeup.BusinessId,
                wakeup.Provider,
                wakeup.UserNumber,
                latest.ProviderMessageId,
                nextDueAtUtc,
                ct);

            _logger.LogInformation(
                "Debounce inbound reprogramado para BusinessId={BusinessId}, UserNumber={UserNumber}, DueAt={DueAtUtc:o}",
                wakeup.BusinessId,
                wakeup.UserNumber,
                nextDueAtUtc);
            return;
        }

        var providerMessageIds = pending.Select(r => r.ProviderMessageId).ToList();
        await _deduplicationService.MarkProcessingAsync(
            wakeup.BusinessId,
            wakeup.Provider,
            providerMessageIds,
            ct);

        try
        {
            var allMessages = await ParsePendingMessagesAsync(wakeup.BusinessId, pending);
            if (allMessages.Count == 0)
            {
                await _deduplicationService.MarkProcessedAsync(
                    wakeup.BusinessId,
                    wakeup.Provider,
                    providerMessageIds,
                    ct);
                return;
            }

            var distinctUserNumbers = allMessages.Select(m => m.UserNumber).Distinct().ToList();
            if (distinctUserNumbers.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Se detectaron mensajes de múltiples usuarios en un debounce: {string.Join(", ", distinctUserNumbers)}");
            }

            var userNumber = allMessages.First().UserNumber;
            var customerName = allMessages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.CustomerName))?.CustomerName;
            var combinedMessage = string.Join("\n", allMessages.Select(m => m.MessageText).Where(t => !string.IsNullOrWhiteSpace(t)));
            var inboundMetadata = new AgentInboundMetadata(
                allMessages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.ProviderMessageId))?.ProviderMessageId,
                allMessages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.ReplyToProviderMessageId))?.ReplyToProviderMessageId,
                allMessages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.InteractivePayload))?.InteractivePayload);

            if (string.IsNullOrWhiteSpace(combinedMessage))
            {
                await _deduplicationService.MarkProcessedAsync(
                    wakeup.BusinessId,
                    wakeup.Provider,
                    providerMessageIds,
                    ct);
                return;
            }

            if (allMessages.Count > 1)
            {
                _logger.LogInformation(
                    "Debounce inbound: unificando {Count} mensajes del usuario {UserNumber} en negocio {BusinessId}",
                    allMessages.Count,
                    userNumber,
                    wakeup.BusinessId);
            }

            await _messageProcessorService.ProcessIncomingMessageAsync(
                wakeup.BusinessId,
                userNumber,
                combinedMessage,
                customerName,
                inboundMetadata);

            await _deduplicationService.MarkProcessedAsync(
                wakeup.BusinessId,
                wakeup.Provider,
                providerMessageIds,
                ct);
        }
        catch (Exception ex)
        {
            await _deduplicationService.MarkFailedAsync(
                wakeup.BusinessId,
                wakeup.Provider,
                providerMessageIds,
                ex.Message,
                ct);

            _logger.LogError(
                ex,
                "Error procesando debounce inbound para BusinessId={BusinessId}, UserNumber={UserNumber}",
                wakeup.BusinessId,
                wakeup.UserNumber);
            throw;
        }
    }

    private async Task<List<IncomingMessage>> ParsePendingMessagesAsync(
        Guid businessId,
        IReadOnlyList<InboundMessageReceipt> pending)
    {
        var allMessages = new List<IncomingMessage>();

        foreach (var receipt in pending.OrderBy(r => r.ReceivedAtUtc).ThenBy(r => r.ProviderMessageId))
        {
            if (string.IsNullOrWhiteSpace(receipt.RawEntryJson))
                continue;

            var entry = JsonSerializer.Deserialize<Entry>(receipt.RawEntryJson, JsonOptions);
            if (entry is null)
                continue;

            var messages = await _webhookParserService.ExtractAllMessagesFromEntryAsync(entry, businessId);
            allMessages.AddRange(messages);
        }

        return allMessages;
    }
}


