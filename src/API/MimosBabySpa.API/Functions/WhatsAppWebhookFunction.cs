using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.API.Functions;

public class WhatsAppWebhookFunction
{
    private const string WhatsAppProvider = "whatsapp";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWhatsAppMessageProcessorService _messageProcessorService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IBusinessIdentificationService _businessIdentificationService;
    private readonly IInboundMessageDeduplicationService _deduplicationService;
    private readonly IWhatsAppInboundQueueService _inboundQueueService;
    private readonly ILogger<WhatsAppWebhookFunction> _logger;

    public WhatsAppWebhookFunction(
        IWhatsAppMessageProcessorService messageProcessorService,
        IWhatsAppService whatsAppService,
        IBusinessIdentificationService businessIdentificationService,
        IInboundMessageDeduplicationService deduplicationService,
        IWhatsAppInboundQueueService inboundQueueService,
        ILogger<WhatsAppWebhookFunction> logger)
    {
        _messageProcessorService = messageProcessorService;
        _whatsAppService = whatsAppService;
        _businessIdentificationService = businessIdentificationService;
        _deduplicationService = deduplicationService;
        _inboundQueueService = inboundQueueService;
        _logger = logger;
    }

    [Function("WhatsAppWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
    {
        try
        {
            if (req.Method == "GET")
            {
                var queryParams = QueryHelpers.ParseQuery(req.Url.Query);
                var mode = queryParams.ContainsKey("hub.mode") ? queryParams["hub.mode"].ToString() : null;
                var token = queryParams.ContainsKey("hub.verify_token") ? queryParams["hub.verify_token"].ToString() : null;
                var challenge = queryParams.ContainsKey("hub.challenge") ? queryParams["hub.challenge"].ToString() : null;

                var verifiedChallenge = await _messageProcessorService.VerifyWebhookAsync(
                    mode ?? string.Empty,
                    token ?? string.Empty,
                    challenge ?? string.Empty);

                if (verifiedChallenge != null)
                {
                    var response = req.CreateResponse(HttpStatusCode.OK);
                    await response.WriteStringAsync(verifiedChallenge);
                    return response;
                }

                return req.CreateResponse(HttpStatusCode.Forbidden);
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var webhookData = JsonSerializer.Deserialize<WhatsAppWebhookDto>(requestBody, JsonOptions);

            if (webhookData?.Entry == null || !webhookData.Entry.Any())
                return req.CreateResponse(HttpStatusCode.OK);

            foreach (var entry in webhookData.Entry)
            {
                var phoneNumberId = entry.Changes?
                    .FirstOrDefault(c => c?.Value?.Metadata != null)?.Value?.Metadata?.PhoneNumberId;

                if (string.IsNullOrWhiteSpace(phoneNumberId))
                {
                    _logger.LogWarning("phone_number_id es nulo en los datos de entrada");
                    continue;
                }

                var businessContext = await _businessIdentificationService.IdentifyBusinessAsync(phoneNumberId);
                if (businessContext == null)
                {
                    _logger.LogWarning("No se pudo identificar el negocio para phone_number_id: {PhoneNumberId}", phoneNumberId);
                    continue;
                }

                var incomingMessages = EnumerateIncomingMessages(entry)
                    .Where(message => !string.IsNullOrWhiteSpace(message.Id) && !string.IsNullOrWhiteSpace(message.From))
                    .ToList();

                var lastMessageId = incomingMessages.LastOrDefault()?.Id;
                if (!string.IsNullOrWhiteSpace(lastMessageId))
                {
                    await _whatsAppService.AcknowledgeMessageAsync(
                        businessContext.WhatsAppNumber.WhatsAppPhoneNumberId,
                        businessContext.WhatsAppNumber.WhatsAppAccessToken,
                        lastMessageId);
                }

                foreach (var message in incomingMessages)
                {
                    var now = DateTime.UtcNow;
                    var dueAtUtc = now.Add(DebounceDelay);
                    var singleMessageEntry = FilterEntryMessages(entry, new HashSet<string>(StringComparer.Ordinal) { message.Id }, false);
                    var customerName = singleMessageEntry.Changes?
                        .SelectMany(c => c.Value?.Contacts ?? [])
                        .FirstOrDefault()?.Profile?.Name;
                    var rawEntryJson = JsonSerializer.Serialize(singleMessageEntry, JsonOptions);

                    var isNew = await _deduplicationService.TryRecordReceivedAsync(
                        businessContext.BusinessId,
                        WhatsAppProvider,
                        message.Id,
                        message.From,
                        customerName,
                        rawEntryJson,
                        now,
                        dueAtUtc);

                    if (!isNew)
                    {
                        _logger.LogInformation(
                            "Mensaje inbound duplicado recibido. BusinessId: {BusinessId}, WhatsAppMessageId: {WhatsAppMessageId}",
                            businessContext.BusinessId,
                            message.Id);
                        // Service Bus duplicate detection is optional at the entity level.
                        // The existing receipt already owns the original wake-up message.
                        continue;
                    }


                    await _inboundQueueService.ScheduleDebounceAsync(
                        businessContext.BusinessId,
                        WhatsAppProvider,
                        message.From,
                        message.Id,
                        dueAtUtc);

                    await _deduplicationService.MarkQueuedAsync(
                        businessContext.BusinessId,
                        WhatsAppProvider,
                        message.Id,
                        dueAtUtc);
                }
            }

            return req.CreateResponse(HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando webhook de WhatsApp");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("Error procesando webhook");
            return errorResponse;
        }
    }

    private static IEnumerable<Message> EnumerateIncomingMessages(Entry entry)
    {
        return entry.Changes?
            .Where(c => c?.Field == "messages" && c.Value?.Messages != null)
            .SelectMany(c => c.Value.Messages) ?? [];
    }

    private static Entry FilterEntryMessages(
        Entry entry,
        IReadOnlySet<string> messageIdsToProcess,
        bool includeMessagesWithoutIds)
    {
        if (messageIdsToProcess.Count == 0 && !includeMessagesWithoutIds)
        {
            return new Entry
            {
                Id = entry.Id,
                Changes = []
            };
        }

        return new Entry
        {
            Id = entry.Id,
            Changes = entry.Changes?
                .Select(change => new Change
                {
                    Field = change.Field,
                    Value = new Value
                    {
                        Metadata = change.Value.Metadata,
                        Contacts = change.Value.Contacts,
                        Messages = change.Value.Messages?
                            .Where(message =>
                                (!string.IsNullOrWhiteSpace(message.Id) && messageIdsToProcess.Contains(message.Id)) ||
                                (includeMessagesWithoutIds && string.IsNullOrWhiteSpace(message.Id)))
                            .ToList() ?? []
                    }
                })
                .Where(change => change.Field == "messages" && change.Value.Messages.Any())
                .ToList() ?? []
        };
    }
}
