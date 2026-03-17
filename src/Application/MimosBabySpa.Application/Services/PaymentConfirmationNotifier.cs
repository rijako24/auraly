using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts.Templates;
using MimosBabySpa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Envía mensajes proactivos al cliente tras confirmación de pago:
/// éxito (reserva creada) o slot tomado (alternativas disponibles).
/// </summary>
public class PaymentConfirmationNotifier
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IMediaUrlResolver _mediaUrlResolver;
    private readonly ILogger<PaymentConfirmationNotifier> _logger;

    public PaymentConfirmationNotifier(
        IWhatsAppService whatsAppService,
        IMediaUrlResolver mediaUrlResolver,
        ILogger<PaymentConfirmationNotifier> logger)
    {
        _whatsAppService = whatsAppService;
        _mediaUrlResolver = mediaUrlResolver;
        _logger = logger;
    }

    public async Task SendAsync(
        ConversationState state,
        LoadedBusinessContext businessContext,
        CancellationToken ct = default)
    {
        var phone = state.Phone?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogDebug("No se envía notificación: teléfono vacío");
            return;
        }

        var messages = BuildMessageList(state, businessContext);

        var index = 0;
        foreach (var message in messages)
        {
            index++;
            try
            {
                await SendMessageItemAsync(state.BusinessId, phone, message, ct);
                _logger.LogDebug("Mensaje de confirmación {Index}/{Total} enviado a {Phone}",
                    index, messages.Count, phone);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error enviando mensaje de confirmación {Index}/{Total} a {Phone}",
                    index, messages.Count, phone);
            }
        }

        _logger.LogInformation("Notificación proactiva enviada a {Phone}: {Count} mensajes", phone, messages.Count);
    }

    private List<SendableMessageItem> BuildMessageList(
        ConversationState state,
        LoadedBusinessContext businessContext)
    {
        var config = businessContext.ConfirmationMessages;

        if (config?.Messages is not { Count: > 0 })
        {
            _logger.LogWarning("Sin configuración de mensajes de confirmación. Enviando fallback mínimo.");
            return new List<SendableMessageItem> { new("¡Tu pago ha sido confirmado y tu reserva creada!", null, null, null) };
        }

        var list = new List<SendableMessageItem>();

        foreach (var m in config.Messages)
        {
            var body = ResolvePlaceholders(m.Body, state, businessContext);
            string? mediaRef = null;
            string? mediaType = null;
            string? filename = null;

            if (m.AttachmentId.HasValue &&
                businessContext.Attachments.TryGetValue(m.AttachmentId.Value, out var attachment))
            {
                mediaRef = attachment.BlobPath;
                mediaType = attachment.MediaType;
                filename = attachment.Filename;
                _logger.LogInformation(
                    "Adjunto resuelto: AttachmentId={AttachmentId}, BlobPath={BlobPath}, MediaType={MediaType}, Filename={Filename}",
                    m.AttachmentId.Value, mediaRef, mediaType, filename);
            }
            else if (m.AttachmentId.HasValue)
            {
                _logger.LogWarning("AttachmentId {AttachmentId} no encontrado en el negocio. Se envía solo el texto.",
                    m.AttachmentId.Value);
            }

            list.Add(new SendableMessageItem(body, mediaRef, mediaType, filename));
        }

        return list;
    }

    private sealed record SendableMessageItem(string? Body, string? MediaRef, string? MediaType, string? Filename);

    private async Task SendMessageItemAsync(
        Guid businessId,
        string phone,
        SendableMessageItem item,
        CancellationToken ct)
    {
        var hasBody = !string.IsNullOrWhiteSpace(item.Body);
        var hasMedia = !string.IsNullOrWhiteSpace(item.MediaRef);

        if (!hasBody && !hasMedia)
        {
            _logger.LogWarning("Mensaje de confirmación sin Body ni MediaRef — omitido");
            return;
        }

        if (!hasMedia)
        {
            await _whatsAppService.SendTextMessageAsync(businessId, phone, item.Body!);
            return;
        }

        _logger.LogInformation(
            "Iniciando envío de adjunto: BusinessId={BusinessId}, Phone={Phone}, MediaRef={MediaRef}, MediaType={MediaType}, Filename={Filename}",
            businessId, phone, item.MediaRef, item.MediaType, item.Filename);

        var publicUrl = await _mediaUrlResolver.ResolveAsync(businessId, item.MediaRef!, ct);

        var urlPreview = publicUrl.Length > 80 ? publicUrl[..80] + "..." : publicUrl;
        _logger.LogInformation(
            "URL del adjunto resuelta (preview): {UrlPreview}, LongitudTotal={Length}",
            urlPreview, publicUrl.Length);

        var caption = item.Body;

        if (string.Equals(item.MediaType, "image", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Enviando imagen vía WhatsApp: Phone={Phone}", phone);
            await _whatsAppService.SendImageMessageAsync(businessId, phone, publicUrl, caption);
        }
        else
        {
            _logger.LogInformation("Enviando documento vía WhatsApp: Phone={Phone}, Filename={Filename}", phone, item.Filename);
            await _whatsAppService.SendDocumentMessageAsync(businessId, phone, publicUrl, caption, item.Filename);
        }
    }

    /// <summary>
    /// Notifica al cliente que el horario elegido ya fue tomado y ofrece alternativas.
    /// </summary>
    public async Task SendSlotTakenAsync(
        ConversationState state,
        string originalTime,
        List<string> availableSlots,
        CancellationToken ct = default)
    {
        var phone = state.Phone?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogDebug("No se envía notificación de slot tomado: teléfono vacío");
            return;
        }

        string message;
        if (availableSlots.Count > 0)
        {
            var slotList = string.Join(", ", availableSlots);
            message = $"Lo sentimos, el horario de las {originalTime} ya fue tomado por otra persona que realizó el pago antes. " +
                      $"Tu pago está registrado y seguro. " +
                      $"Horarios disponibles para esa fecha: {slotList}. " +
                      $"Responde con la hora que prefieras para confirmar tu reserva.";
        }
        else
        {
            message = $"Lo sentimos, el horario de las {originalTime} ya fue tomado y no hay más disponibilidad para ese día. " +
                      $"Tu pago está registrado y seguro. " +
                      $"Por favor indícanos otra fecha para verificar disponibilidad.";
        }

        try
        {
            await _whatsAppService.SendTextMessageAsync(state.BusinessId, phone, message);
            _logger.LogInformation("Notificación de slot tomado enviada a {Phone}", phone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando notificación de slot tomado a {Phone}", phone);
        }
    }

    private static string? ResolvePlaceholders(
        string? template,
        ConversationState state,
        LoadedBusinessContext businessContext)
    {
        if (string.IsNullOrWhiteSpace(template))
            return template;

        var total = ReservationTotalCalculator.Calculate(state, businessContext.Services, businessContext.AddOnRules);

        return template
            .Replace("{CustomerName}", state.CustomerName ?? "")
            .Replace("{Service}", state.Service ?? "")
            .Replace("{Date}", state.DesiredDate?.ToString("dd/MM/yyyy") ?? "")
            .Replace("{Time}", state.DesiredTime?.ToString("HH:mm") ?? "")
            .Replace("{Total}", total.ToString("N0"));
    }
}
