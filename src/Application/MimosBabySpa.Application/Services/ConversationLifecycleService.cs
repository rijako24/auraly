using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public interface IConversationLifecycleService
{
    Task<Conversation> GetOrOpenForCustomerAsync(
        Guid businessId,
        string userNumber,
        string? channelCustomerName = null,
        CancellationToken ct = default);

    Task CloseAsync(Guid conversationId, string closeReason, CancellationToken ct = default);

    Task TouchActivityAsync(Guid conversationId, string? lastMessage, CancellationToken ct = default);
}

public sealed class ConversationLifecycleService : IConversationLifecycleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _businessClock;
    private readonly IEnumerable<IConversationClosedHook> _closedHooks;
    private readonly ILogger<ConversationLifecycleService> _logger;

    public ConversationLifecycleService(
        IUnitOfWork unitOfWork,
        IBusinessClock businessClock,
        IEnumerable<IConversationClosedHook> closedHooks,
        ILogger<ConversationLifecycleService> logger)
    {
        _unitOfWork = unitOfWork;
        _businessClock = businessClock;
        _closedHooks = closedHooks;
        _logger = logger;
    }

    public async Task<Conversation> GetOrOpenForCustomerAsync(
        Guid businessId,
        string userNumber,
        string? channelCustomerName = null,
        CancellationToken ct = default)
    {
        var lead = await _unitOfWork.Leads.GetByBusinessIdAndUserNumberAsync(businessId, userNumber);
        var active = await _unitOfWork.Conversations.GetActiveByBusinessIdAndUserNumberAsync(businessId, userNumber, ct);
        var clock = await _businessClock.GetSnapshotAsync(businessId, ct);

        if (active is not null && HasDayChanged(active.LastActivityAt, clock))
        {
            _logger.LogInformation(
                "Closing conversation {ConvId} for {UserNumber}: business day changed",
                active.ConversationId, userNumber);
            await CloseInternalAsync(active, ConversationCloseReasons.DayChanged, ct);
            active = null;
        }

        if (active is not null)
            return active;

        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            CustomerName = lead?.CustomerName ?? channelCustomerName,
            CustomerEmail = lead?.CustomerEmail,
            Status = ConversationLifecycleStatus.Active,
            OpenedAt = now,
            LastActivityAt = now,
            Timestamp = now
        };

        var created = await _unitOfWork.Conversations.CreateAsync(conversation);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Opened conversation {ConvId} for {UserNumber} in business {BusinessId}",
            created.ConversationId, userNumber, businessId);

        return created;
    }

    public async Task CloseAsync(Guid conversationId, string closeReason, CancellationToken ct = default)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation is null || conversation.Status == ConversationLifecycleStatus.Closed)
            return;

        await CloseInternalAsync(conversation, closeReason, ct);
    }

    public async Task TouchActivityAsync(Guid conversationId, string? lastMessage, CancellationToken ct = default)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation is null)
            return;

        var now = DateTime.UtcNow;
        conversation.LastActivityAt = now;
        conversation.Timestamp = now;
        if (!string.IsNullOrWhiteSpace(lastMessage))
            conversation.LastMessage = lastMessage;

        await _unitOfWork.Conversations.UpdateAsync(conversation);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task CloseInternalAsync(Conversation conversation, string closeReason, CancellationToken ct)
    {
        if (conversation.Status == ConversationLifecycleStatus.Closed)
            return;

        conversation.Status = ConversationLifecycleStatus.Closed;
        conversation.ClosedAt = DateTime.UtcNow;
        conversation.CloseReason = closeReason;

        await _unitOfWork.Conversations.UpdateAsync(conversation);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Closed conversation {ConvId} reason={Reason}",
            conversation.ConversationId, closeReason);

        foreach (var hook in _closedHooks)
        {
            try
            {
                await hook.OnClosedAsync(conversation, closeReason, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Closed-hook failed for conversation {ConvId}",
                    conversation.ConversationId);
            }
        }
    }

    private static bool HasDayChanged(DateTime lastActivityUtc, BusinessClockSnapshot clock)
    {
        var lastBusinessDay = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(lastActivityUtc, clock.TimeZone));
        return clock.Today > lastBusinessDay;
    }
}
