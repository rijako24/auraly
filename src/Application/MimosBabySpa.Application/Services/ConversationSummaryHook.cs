using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Al cerrar un episodio conversacional, agrega una línea determinista al resumen rodante del cliente.
/// </summary>
public sealed class ConversationSummaryHook : IConversationClosedHook
{
    private const int MaxSummaryLines = 5;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerMemoryService _customerMemory;
    private readonly ILogger<ConversationSummaryHook> _logger;

    public ConversationSummaryHook(
        IUnitOfWork unitOfWork,
        ICustomerMemoryService customerMemory,
        ILogger<ConversationSummaryHook> logger)
    {
        _unitOfWork = unitOfWork;
        _customerMemory = customerMemory;
        _logger = logger;
    }

    public async Task OnClosedAsync(Conversation conversation, string closeReason, CancellationToken ct = default)
    {
        var episodeLine = await BuildEpisodeLineAsync(conversation, closeReason, ct);
        if (string.IsNullOrWhiteSpace(episodeLine))
            return;

        var existing = await _customerMemory.GetAsync(
            conversation.BusinessId, conversation.UserNumber, CustomerMemoryKeys.Summary, ct);

        var lines = string.IsNullOrWhiteSpace(existing)
            ? new List<string>()
            : existing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        lines.Insert(0, episodeLine);

        if (lines.Count > MaxSummaryLines)
            lines = lines.Take(MaxSummaryLines).ToList();

        var rolled = string.Join(Environment.NewLine, lines);

        await _customerMemory.RememberAsync(
            conversation.BusinessId,
            conversation.UserNumber,
            CustomerMemoryKeys.Summary,
            rolled,
            ct);

        _logger.LogInformation(
            "Customer summary updated for {UserNumber} in business {BusinessId}: {Line}",
            conversation.UserNumber, conversation.BusinessId, episodeLine);
    }

    private async Task<string?> BuildEpisodeLineAsync(
        Conversation conversation, string closeReason, CancellationToken ct)
    {
        var closedDate = (conversation.ClosedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd");

        var reservation = await _unitOfWork.Reservations.GetActiveByConversationIdAsync(
            conversation.ConversationId, ct);

        if (reservation is not null
            && reservation.Status is ReservationStatus.Confirmed or ReservationStatus.OnHold)
        {
            var serviceName = reservation.Service?.ServiceName ?? reservation.GetServiceName();
            if (reservation.ReservationDateTime is not null)
            {
                var date = DateOnly.FromDateTime(reservation.ReservationDateTime.Value);
                var time = TimeOnly.FromDateTime(reservation.ReservationDateTime.Value);
                return $"{closedDate}: reservó {serviceName} para {date:yyyy-MM-dd} {time:HH:mm}.";
            }

            if (!string.IsNullOrWhiteSpace(serviceName))
                return $"{closedDate}: reservó {serviceName}.";
        }

        var facts = await _unitOfWork.ConversationContexts.GetByConversationIdAsync(conversation.ConversationId);
        var factMap = facts.ToDictionary(f => f.Field, f => f.Value, StringComparer.OrdinalIgnoreCase);

        var service = ConversationFactKeys.Get(factMap, ConversationFactKeys.Service);
        var desiredDate = ConversationFactKeys.Get(factMap, ConversationFactKeys.DesiredDate);

        if (closeReason == ConversationCloseReasons.DayChanged)
        {
            var intent = BuildIntentFragment(service, desiredDate);
            return string.IsNullOrWhiteSpace(intent)
                ? $"{closedDate}: conversación cerrada por cambio de día (sin reserva)."
                : $"{closedDate}: consultó {intent}; no reservó (cierre por cambio de día).";
        }

        if (closeReason == ConversationCloseReasons.ReservationConfirmed)
            return $"{closedDate}: reserva confirmada.";

        if (closeReason == ConversationCloseReasons.UserCancelled)
            return $"{closedDate}: conversación cancelada por el usuario.";

        if (closeReason == ConversationCloseReasons.Manual)
            return $"{closedDate}: conversación cerrada manualmente.";

        if (closeReason == ConversationCloseReasons.Timeout)
            return $"{closedDate}: conversación cerrada por inactividad.";

        var genericIntent = BuildIntentFragment(service, desiredDate);
        return string.IsNullOrWhiteSpace(genericIntent)
            ? $"{closedDate}: conversación cerrada ({closeReason})."
            : $"{closedDate}: consultó {genericIntent}; conversación cerrada ({closeReason}).";
    }

    private static string BuildIntentFragment(string? service, string? desiredDate)
    {
        if (!string.IsNullOrWhiteSpace(service) && !string.IsNullOrWhiteSpace(desiredDate))
            return $"{service} para {desiredDate}";

        if (!string.IsNullOrWhiteSpace(service))
            return service;

        if (!string.IsNullOrWhiteSpace(desiredDate))
            return $"cita para {desiredDate}";

        return string.Empty;
    }
}
