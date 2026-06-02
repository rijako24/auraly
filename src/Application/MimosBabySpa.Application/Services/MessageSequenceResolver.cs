using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

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
                var body = ResolvePlaceholders(step.Body, reservation, context.Payment, services, addOnRules);
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

                result.Add(new OutboundMessage(body, mediaUrl, mediaType, filename));
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

            resolved = resolved
                .Replace("{CustomerName}", reservation.CustomerNameSnapshot ?? string.Empty)
                .Replace("{Service}", serviceName)
                .Replace("{Date}", date)
                .Replace("{Time}", time)
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

        return resolved;
    }
}
