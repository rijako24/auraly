using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using ConversationState = MimosBabySpa.Domain.Models.ConversationState;

namespace MimosBabySpa.Application.Services;

public class PaymentConfirmationNotifier
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IMediaUrlResolver _mediaUrlResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PaymentConfirmationNotifier> _logger;

    public PaymentConfirmationNotifier(
        IWhatsAppService whatsAppService,
        IMediaUrlResolver mediaUrlResolver,
        IUnitOfWork unitOfWork,
        ILogger<PaymentConfirmationNotifier> logger)
    {
        _whatsAppService = whatsAppService;
        _mediaUrlResolver = mediaUrlResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task SendAsync(ConversationState state, Reservation reservation, CancellationToken ct = default)
    {
        var phone = reservation.CustomerPhoneSnapshot?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogDebug("No se envía notificación: teléfono vacío");
            return;
        }

        var config = await LoadConfirmationMessagesAsync(state.BusinessId);
        var attachments = await LoadAttachmentsAsync(state.BusinessId);
        var services = await LoadServicesAsync(state.BusinessId);
        var addOnRules = await LoadAddOnRulesAsync(state.BusinessId);

        var messages = BuildMessageList(reservation, config, attachments, services, addOnRules);

        var index = 0;
        foreach (var message in messages)
        {
            index++;
            try
            {
                await SendMessageItemAsync(state.BusinessId, phone, message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando mensaje de confirmación {Index}/{Total}", index, messages.Count);
            }
        }
    }

    public async Task SendSlotTakenAsync(
        ConversationState state,
        Reservation reservation,
        string originalTime,
        List<string> availableSlots,
        CancellationToken ct = default)
    {
        await SendSlotTakenMessageAsync(state, reservation, originalTime, availableSlots, ct);
    }

    public async Task SendPaymentConfirmedAndSlotTakenAsync(
        ConversationState state,
        PaymentTransaction payment,
        Reservation reservation,
        string originalTime,
        List<string> availableSlots,
        CancellationToken ct = default)
    {
        var phone = reservation.CustomerPhoneSnapshot?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
            return;

        var amount = payment.AmountInCents / 100m;
        var receipt = $"✅ Recibimos tu pago de ${amount:N0} {payment.Currency}. Tu comprobante quedó registrado.";

        try
        {
            await _whatsAppService.SendTextMessageAsync(state.BusinessId, phone, receipt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando recibo de pago confirmado");
        }

        await SendSlotTakenMessageAsync(state, reservation, originalTime, availableSlots, ct);
    }

    private async Task SendSlotTakenMessageAsync(
        ConversationState state,
        Reservation reservation,
        string originalTime,
        List<string> availableSlots,
        CancellationToken ct)
    {
        var phone = reservation.CustomerPhoneSnapshot?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
            return;

        string message = availableSlots.Count > 0
            ? $"Lo sentimos, el horario de las {originalTime} ya no está disponible porque otro cliente lo reservó primero. " +
              $"Tu pago está seguro. ¿Quieres elegir otro horario? Opciones: {string.Join(", ", availableSlots)}."
            : $"Lo sentimos, el horario de las {originalTime} ya no está disponible y no hay más cupos ese día. " +
              "Tu pago está seguro — escríbenos para elegir otra fecha.";

        try
        {
            await _whatsAppService.SendTextMessageAsync(state.BusinessId, phone, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando notificación de slot tomado");
        }
    }

    private async Task<PaymentConfirmationMessagesConfig?> LoadConfirmationMessagesAsync(Guid businessId)
    {
        try
        {
            var config = await _unitOfWork.BusinessConfigurations
                .GetByBusinessIdAndKeyAsync(businessId, BusinessConfigurationKey.PaymentConfirmationMessages);
            if (config?.Value is null || !config.Value.TrimStart().StartsWith('{'))
                return null;
            return JsonSerializer.Deserialize<PaymentConfirmationMessagesConfig>(
                config.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Error deserializando PaymentConfirmationMessages");
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<Guid, (string BlobPath, string? MediaType, string? Filename)>> LoadAttachmentsAsync(Guid businessId)
    {
        var attachments = await _unitOfWork.BusinessAttachments.GetByBusinessIdAsync(businessId);
        return attachments.Where(a => a.IsActive).ToDictionary(
            a => a.BusinessAttachmentId,
            a => (a.BlobPath, (string?)a.MediaType, (string?)a.Filename));
    }

    private async Task<List<ServiceInfo>> LoadServicesAsync(Guid businessId)
    {
        var services = await _unitOfWork.Services.GetActiveByBusinessIdAsync(businessId);
        return services.Select(s => new ServiceInfo { Name = s.ServiceName, Price = s.Price }).ToList();
    }

    private async Task<List<AddOnRuleInfo>> LoadAddOnRulesAsync(Guid businessId)
    {
        var rules = await _unitOfWork.ServiceAddOnRules.GetByBusinessIdAsync(businessId);
        return rules.Select(r => new AddOnRuleInfo
        {
            AddOnName = r.AddOnService.ServiceName,
            AddOnPrice = r.AddOnService.Price
        }).ToList();
    }

    private List<SendableMessageItem> BuildMessageList(
        Reservation reservation,
        PaymentConfirmationMessagesConfig? config,
        IReadOnlyDictionary<Guid, (string BlobPath, string? MediaType, string? Filename)> attachments,
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules)
    {
        if (config?.Messages is not { Count: > 0 })
            return [new("¡Tu pago ha sido confirmado y tu reserva creada!", null, null, null)];

        return config.Messages.Select(m =>
        {
            var body = ResolvePlaceholders(m.Body, reservation, services, addOnRules);
            string? mediaRef = null, mediaType = null, filename = null;
            if (m.AttachmentId.HasValue && attachments.TryGetValue(m.AttachmentId.Value, out var att))
            {
                mediaRef = att.BlobPath;
                mediaType = att.MediaType;
                filename = att.Filename;
            }
            return new SendableMessageItem(body, mediaRef, mediaType, filename);
        }).ToList();
    }

    private sealed record SendableMessageItem(string? Body, string? MediaRef, string? MediaType, string? Filename);

    private async Task SendMessageItemAsync(Guid businessId, string phone, SendableMessageItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Body) && string.IsNullOrWhiteSpace(item.MediaRef))
            return;

        if (string.IsNullOrWhiteSpace(item.MediaRef))
        {
            await _whatsAppService.SendTextMessageAsync(businessId, phone, item.Body!);
            return;
        }

        var publicUrl = await _mediaUrlResolver.ResolveAsync(businessId, item.MediaRef!, ct);
        if (string.Equals(item.MediaType, "image", StringComparison.OrdinalIgnoreCase))
            await _whatsAppService.SendImageMessageAsync(businessId, phone, publicUrl, item.Body);
        else
            await _whatsAppService.SendDocumentMessageAsync(businessId, phone, publicUrl, item.Body, item.Filename);
    }

    private static string? ResolvePlaceholders(
        string? template,
        Reservation reservation,
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;

        var serviceName = reservation.Service?.ServiceName ?? string.Empty;
        var addOnNames = reservation.AddOns
            .Select(a => a.AddOnService?.ServiceName)
            .Where(n => !string.IsNullOrWhiteSpace(n));
        var addOnsCsv = string.Join(", ", addOnNames!);
        var total = ReservationTotalCalculator.Calculate(serviceName, addOnsCsv, services, addOnRules);

        var date = reservation.ReservationDateTime.HasValue
            ? DateOnly.FromDateTime(reservation.ReservationDateTime.Value).ToString("dd/MM/yyyy")
            : string.Empty;
        var time = reservation.ReservationDateTime.HasValue
            ? TimeOnly.FromDateTime(reservation.ReservationDateTime.Value).ToString("HH:mm")
            : string.Empty;

        return template
            .Replace("{CustomerName}", reservation.CustomerNameSnapshot ?? "")
            .Replace("{Service}", serviceName)
            .Replace("{Date}", date)
            .Replace("{Time}", time)
            .Replace("{Total}", total.ToString("N0"));
    }
}
