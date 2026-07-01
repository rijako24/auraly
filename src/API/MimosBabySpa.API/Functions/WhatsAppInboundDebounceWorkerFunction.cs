using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Infrastructure.Services;

namespace MimosBabySpa.API.Functions;

public sealed class WhatsAppInboundDebounceWorkerFunction
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IInboundMessageDeduplicationService _deduplicationService;
    private readonly IWhatsAppInboundQueueService _queueService;
    private readonly IWhatsAppWebhookParserService _webhookParserService;
    private readonly IInboundMessageBatchProcessor _batchProcessor;
    private readonly ILogger<WhatsAppInboundDebounceWorkerFunction> _logger;

    public WhatsAppInboundDebounceWorkerFunction(
        IInboundMessageDeduplicationService deduplicationService,
        IWhatsAppInboundQueueService queueService,
        IWhatsAppWebhookParserService webhookParserService,
        IInboundMessageBatchProcessor batchProcessor,
        ILogger<WhatsAppInboundDebounceWorkerFunction> logger)
    {
        _deduplicationService = deduplicationService;
        _queueService = queueService;
        _webhookParserService = webhookParserService;
        _batchProcessor = batchProcessor;
        _logger = logger;
    }

    [Function("WhatsAppInboundDebounceWorker")]
    public async Task Run(
        [ServiceBusTrigger(WhatsAppInboundQueueService.DefaultQueueName, Connection = "ServiceBusConnection", IsSessionsEnabled = true)] string body,
        CancellationToken ct)
    {
        var wakeup = JsonSerializer.Deserialize<WhatsAppInboundDebounceMessage>(body, JsonOptions)
            ?? throw new InvalidOperationException("Mensaje de debounce inbound invalido.");

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
            if (allMessages.Count > 0)
            {
                var result = await _batchProcessor.ProcessAsync(
                    wakeup.BusinessId,
                    allMessages,
                    ct);

                if (allMessages.Count > 1)
                {
                    _logger.LogInformation(
                        "Debounce inbound: procesado lote de {Count} mensajes con {InteractiveCount} mensaje(s) interactivo(s) en negocio {BusinessId}",
                        result.MessageCount,
                        result.InteractiveMessageCount,
                        wakeup.BusinessId);
                }
            }

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
