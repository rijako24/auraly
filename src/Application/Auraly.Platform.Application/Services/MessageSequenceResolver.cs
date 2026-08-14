using System.Globalization;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public sealed class MessageSequenceResolver : IMessageSequenceResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediaUrlResolver _mediaUrlResolver;
    private readonly ILogger<MessageSequenceResolver> _logger;

    public MessageSequenceResolver(
        IUnitOfWork unitOfWork,
        IMediaUrlResolver mediaUrlResolver,
        ILogger<MessageSequenceResolver> logger)
    {
        _unitOfWork = unitOfWork;
        _mediaUrlResolver = mediaUrlResolver;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OutboundMessage>> ResolveAsync(
        Guid businessId,
        string sequenceName,
        MessageSequenceCatalog catalog,
        MessageSequenceContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sequenceName)
            || !catalog.TryGetValue(sequenceName, out var sequence)
            || sequence.Messages is not { Count: > 0 } messages)
        {
            _logger.LogDebug(
                "Secuencia '{Sequence}' no encontrada o vacía para BusinessId={BusinessId}",
                sequenceName,
                businessId);
            return [];
        }

        var reservation = await LoadReservationAsync(context.Reservation, ct);
        var attachments = await LoadAttachmentsAsync(businessId);
        var services = await LoadServicesAsync(businessId);
        var addOnRules = await LoadAddOnRulesAsync(businessId);

        var result = new List<OutboundMessage>(messages.Count);

        foreach (var step in messages)
        {
            try
            {
                if (string.Equals(step.Type, "whatsapp_template", StringComparison.OrdinalIgnoreCase))
                {
                    var template = ResolveTemplate(step, reservation, context.Payment, context.Custom, services, addOnRules);
                    if (template is not null)
                        result.Add(new OutboundMessage(null, null, "template", Template: template, Buttons: ResolveButtons(step.Buttons, reservation, context.Payment, context.Custom, services, addOnRules)));
                    continue;
                }

                var body = ResolvePlaceholders(step.Body, reservation, context.Payment, context.Custom, services, addOnRules);
                string? mediaUrl = null;
                string mediaType = "text";
                string? filename = null;

                if (step.AttachmentId.HasValue
                    && attachments.TryGetValue(step.AttachmentId.Value, out var attachment))
                {
                    mediaUrl = await _mediaUrlResolver.ResolveAsync(businessId, attachment.BlobPath, ct);
                    mediaType = attachment.MediaType ?? "document";
                    filename = attachment.Filename;
                }

                if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(mediaUrl))
                    continue;

                var buttons = ResolveButtons(step.Buttons, reservation, context.Payment, context.Custom, services, addOnRules);
                result.Add(new OutboundMessage(body, mediaUrl, mediaType, filename, buttons));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error resolviendo paso de secuencia '{Sequence}' para BusinessId={BusinessId}",
                    sequenceName,
                    businessId);
            }
        }

        return result;
    }

    private static WhatsAppTemplateMessage? ResolveTemplate(
        MessageSequenceStep step,
        Reservation? reservation,
        PaymentSequenceContext? payment,
        IReadOnlyDictionary<string, string> custom,
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules)
    {
        if (string.IsNullOrWhiteSpace(step.TemplateName))
            return null;

        var headerParameters = step.HeaderParameters
            .Select(p => ResolvePlaceholders(p, reservation, payment, custom, services, addOnRules) ?? string.Empty)
            .ToList();

        var bodyParameters = step.BodyParameters
            .Select(p => ResolvePlaceholders(p, reservation, payment, custom, services, addOnRules) ?? string.Empty)
            .ToList();

        return new WhatsAppTemplateMessage(
            step.TemplateName.Trim(),
            string.IsNullOrWhiteSpace(step.Language) ? "es_CO" : step.Language.Trim(),
            headerParameters,
            bodyParameters);
    }

    private static IReadOnlyList<OutboundButton> ResolveButtons(
        IReadOnlyList<MessageSequenceButton> buttons,
        Reservation? reservation,
        PaymentSequenceContext? payment,
        IReadOnlyDictionary<string, string> custom,
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules)
    {
        if (buttons.Count == 0)
            return [];

        return buttons
            .Select(button => new OutboundButton(
                ResolvePlaceholders(button.Id, reservation, payment, custom, services, addOnRules) ?? string.Empty,
                ResolvePlaceholders(button.Title, reservation, payment, custom, services, addOnRules) ?? string.Empty))
            .Where(button => !string.IsNullOrWhiteSpace(button.Id) && !string.IsNullOrWhiteSpace(button.Title))
            .Take(3)
            .ToList();
    }

    private async Task<Reservation?> LoadReservationAsync(Reservation? reservation, CancellationToken ct)
    {
        if (reservation is null)
            return null;

        if (reservation.ReservationId == Guid.Empty)
            return reservation;

        var loaded = await _unitOfWork.Reservations.GetByIdAsync(reservation.ReservationId);
        return loaded ?? reservation;
    }

    private async Task<IReadOnlyDictionary<Guid, (string BlobPath, string? MediaType, string? Filename)>> LoadAttachmentsAsync(
        Guid businessId)
    {
        var attachments = await _unitOfWork.BusinessAttachments.GetByBusinessIdAsync(businessId);
        return attachments
            .Where(a => a.IsActive)
            .ToDictionary(
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

    private static string? ResolvePlaceholders(
        string? template,
        Reservation? reservation,
        PaymentSequenceContext? payment,
        IReadOnlyDictionary<string, string> custom,
        List<ServiceInfo> services,
        List<AddOnRuleInfo> addOnRules)
    {
        if (string.IsNullOrWhiteSpace(template))
            return template;

        var resolved = template;

        if (reservation is not null)
        {
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
            var time12 = reservation.ReservationDateTime.HasValue
                ? TimeOnly.FromDateTime(reservation.ReservationDateTime.Value).ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant()
                : string.Empty;

            resolved = resolved
                .Replace("{CustomerName}", reservation.CustomerNameSnapshot ?? string.Empty)
                .Replace("{Service}", serviceName)
                .Replace("{Date}", date)
                .Replace("{Time}", time)
                .Replace("{Time12}", time12)
                .Replace("{Total}", total.ToString("N0"));
        }

        if (payment is not null)
        {
            var slots = payment.AvailableSlots.Count > 0
                ? string.Join(", ", payment.AvailableSlots)
                : string.Empty;

            resolved = resolved
                .Replace("{amount}", payment.Amount.ToString("N0"))
                .Replace("{currency}", payment.Currency)
                .Replace("{slots}", slots)
                .Replace("{Time}", payment.OriginalTime ?? string.Empty);
        }

        foreach (var (key, value) in custom)
        {
            var replacement = value ?? string.Empty;
            resolved = resolved
                .Replace("{" + key + "}", replacement)
                .Replace("{" + ToPascalPlaceholder(key) + "}", replacement);
        }

        return resolved;
    }

    private static string ToPascalPlaceholder(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var parts = key.Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return key.Trim();

        return string.Concat(parts.Select(part =>
            part.Length == 0
                ? string.Empty
                : char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
