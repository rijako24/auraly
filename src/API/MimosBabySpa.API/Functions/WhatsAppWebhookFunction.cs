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
    private readonly IWhatsAppMessageProcessorService _messageProcessorService;
    private readonly IWhatsAppWebhookParserService _webhookParserService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IBusinessIdentificationService _businessIdentificationService;
    private readonly ILogger<WhatsAppWebhookFunction> _logger;

    public WhatsAppWebhookFunction(
        IWhatsAppMessageProcessorService messageProcessorService,
        IWhatsAppWebhookParserService webhookParserService,
        IWhatsAppService whatsAppService,
        IBusinessIdentificationService businessIdentificationService,
        ILogger<WhatsAppWebhookFunction> logger)
    {
        _messageProcessorService = messageProcessorService;
        _webhookParserService = webhookParserService;
        _whatsAppService = whatsAppService;
        _businessIdentificationService = businessIdentificationService;
        _logger = logger;
    }

    [Function("WhatsAppWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
    {
        try
        {
            // Verificación del webhook (GET)
            if (req.Method == "GET")
            {
                var queryParams = QueryHelpers.ParseQuery(req.Url.Query);
                var mode = queryParams.ContainsKey("hub.mode") ? queryParams["hub.mode"].ToString() : null;
                var token = queryParams.ContainsKey("hub.verify_token") ? queryParams["hub.verify_token"].ToString() : null;
                var challenge = queryParams.ContainsKey("hub.challenge") ? queryParams["hub.challenge"].ToString() : null;

                var verifiedChallenge = await _messageProcessorService.VerifyWebhookAsync(
                    mode ?? "", 
                    token ?? "", 
                    challenge ?? "");

                if (verifiedChallenge != null)
                {
                    var response = req.CreateResponse(HttpStatusCode.OK);
                    await response.WriteStringAsync(verifiedChallenge);
                    return response;
                }

                return req.CreateResponse(HttpStatusCode.Forbidden);
            }

            // Procesamiento de mensajes (POST)
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var webhookData = JsonSerializer.Deserialize<WhatsAppWebhookDto>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (webhookData == null || webhookData.Entry == null || !webhookData.Entry.Any())
            {
                return req.CreateResponse(HttpStatusCode.OK);
            }

            foreach (var entry in webhookData.Entry)
            {
                // phone_number_id: value.metadata.phone_number_id (WhatsApp Cloud API) o entry.Id (fallback)
                var phoneNumberId = entry.Changes?
                    .FirstOrDefault(c => c?.Value?.Metadata != null)?.Value?.Metadata?.PhoneNumberId;

                if (string.IsNullOrEmpty(phoneNumberId))
                {
                    _logger.LogWarning("phone_number_id es nulo en los datos de entrada");
                    continue; // Saltar este entry si no se puede identificar el negocio
                }

                var businessContext = await _businessIdentificationService.IdentifyBusinessAsync(phoneNumberId);

                if (businessContext == null)
                {
                    _logger.LogWarning("No se pudo identificar el negocio para phone_number_id: {PhoneNumberId}", phoneNumberId);
                    continue; // Saltar este entry si no se puede identificar el negocio
                }

                // Acuse de recibo inmediato (read + typing). Usa credenciales ya cargadas — sin query extra, seguro fire-and-forget.
                var lastMessageId = entry.Changes?
                    .Where(c => c?.Field == "messages" && c.Value?.Messages != null)
                    .SelectMany(c => c.Value!.Messages)
                    .LastOrDefault()?.Id;
                if (!string.IsNullOrEmpty(lastMessageId))
                    _ = _whatsAppService.AcknowledgeMessageAsync(
                        businessContext.WhatsAppNumber.WhatsAppPhoneNumberId,
                        businessContext.WhatsAppNumber.WhatsAppAccessToken,
                        lastMessageId);

                // Extraer todos los mensajes (texto y audio) de esta entrada específica.
                // Los audios ya están transcritos dentro de ExtractAllMessagesFromEntryAsync
                var allMessages = (await _webhookParserService.ExtractAllMessagesFromEntryAsync(entry, businessContext.BusinessId)).ToList();

                if (!allMessages.Any())
                { 
                    _logger.LogWarning("No hay mensajes para procesar (puede ser otro tipo de evento) para phone_number_id: {PhoneNumberId}", phoneNumberId);
                    continue;
                }

                // Validar que todos los mensajes sean del mismo usuario
                var distinctUserNumbers = allMessages.Select(m => m.UserNumber).Distinct().ToList();
                if (distinctUserNumbers.Count > 1)
                {
                    var errorMessage = $"Se detectaron mensajes de múltiples usuarios ({distinctUserNumbers.Count} usuarios: {string.Join(", ", distinctUserNumbers)}) en el mismo entry. " +
                        "Esto no debería ocurrir normalmente.";
                    _logger.LogError(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                // Como todos los mensajes son del mismo usuario, combinamos todos los mensajes (texto + transcritos) en uno solo
                var userNumber = allMessages.First().UserNumber;
                var customerName = allMessages.First().CustomerName;
                var combinedMessage = string.Join("\n", allMessages.Select(m => m.MessageText));

                // Log si se combinaron múltiples mensajes
                if (allMessages.Count > 1)
                {
                    _logger.LogInformation("Unificando {Count} mensajes del usuario {UserNumber} en un solo mensaje para la IA", 
                        allMessages.Count, userNumber);
                }

                try
                {
                    await _messageProcessorService.ProcessIncomingMessageAsync(
                        businessContext,
                        userNumber,
                        combinedMessage,
                        customerName);
                }
                catch (Exception ex)
                {
                    // Log del error pero continuar con el siguiente Entry
                    _logger.LogError(ex, "Error procesando mensaje unificado del usuario {UserNumber} en negocio {BusinessId}", 
                        userNumber, businessContext.BusinessId);
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
}
