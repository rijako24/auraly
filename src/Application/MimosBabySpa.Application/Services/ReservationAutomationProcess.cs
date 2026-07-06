using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class ReservationAutomationProcess : ITimedProcess
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReservationLookAhead = TimeSpan.FromDays(45);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentRepository _agents;
    private readonly IAgentConfigProvider _configProvider;
    private readonly IBusinessClock _businessClock;
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IOutboundMessageDispatcher _dispatcher;
    private readonly ILogger<ReservationAutomationProcess> _logger;

    public ReservationAutomationProcess(
        IUnitOfWork unitOfWork,
        IAgentRepository agents,
        IAgentConfigProvider configProvider,
        IBusinessClock businessClock,
        IMessageSequenceResolver sequenceResolver,
        IOutboundMessageDispatcher dispatcher,
        ILogger<ReservationAutomationProcess> logger)
    {
        _unitOfWork = unitOfWork;
        _agents = agents;
        _configProvider = configProvider;
        _businessClock = businessClock;
        _sequenceResolver = sequenceResolver;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public string Name => "reservation_automation";

    public async Task RunAsync(CancellationToken ct = default)
    {
        await ReconcileJobsAsync(ct);
        await ProcessDueJobsAsync(ct);
    }

    private async Task ReconcileJobsAsync(CancellationToken ct)
    {
        var activeAgents = await _agents.GetActiveAsync(ct);

        foreach (var agent in activeAgents)
        {
            var config = await _configProvider.GetConfigAsync(agent.AgentId, ct);
            var automations = EnumerateEnabledAutomations(config).ToList();
            if (automations.Count == 0)
                continue;

            var clock = await _businessClock.GetSnapshotAsync(config.BusinessId, ct);
            var fromLocal = clock.Now.DateTime.AddHours(-1);
            var toLocal = clock.Now.DateTime.Add(ReservationLookAhead);
            var reservations = await _unitOfWork.Reservations.GetUpcomingConfirmedByBusinessIdAsync(
                config.BusinessId,
                fromLocal,
                toLocal,
                ct);

            if (reservations.Count == 0)
                continue;

            var utcNow = clock.Now.UtcDateTime;
            var candidates = new List<(Reservation Reservation, ScheduledAutomationJobType Type, DateTime ScheduledAtUtc, string Sequence)>();
            foreach (var reservation in reservations)
            {
                if (!reservation.ReservationDateTime.HasValue)
                    continue;

                foreach (var automation in automations)
                {
                    var scheduledAtUtc = TryCalculateScheduledAtUtc(
                        reservation.ReservationDateTime.Value,
                        automation.Config.Trigger,
                        clock.TimeZone);
                    if (!scheduledAtUtc.HasValue)
                        continue;

                    if (scheduledAtUtc.Value < utcNow)
                        continue;

                    candidates.Add((reservation, automation.Type, scheduledAtUtc.Value, automation.Config.SendMessageSequence!));
                }
            }

            if (candidates.Count == 0)
                continue;

            var keys = candidates
                .Select(c => BuildDeduplicationKey(config.BusinessId, c.Reservation, c.Type, c.Sequence))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var existing = await _unitOfWork.ScheduledAutomationJobs.GetByDeduplicationKeysAsync(keys, ct);

            foreach (var candidate in candidates)
            {
                var key = BuildDeduplicationKey(config.BusinessId, candidate.Reservation, candidate.Type, candidate.Sequence);
                if (existing.ContainsKey(key))
                    continue;

                await _unitOfWork.ScheduledAutomationJobs.AddAsync(new ScheduledAutomationJob
                {
                    ScheduledAutomationJobId = Guid.NewGuid(),
                    BusinessId = config.BusinessId,
                    ReservationId = candidate.Reservation.ReservationId,
                    AgentId = config.AgentId,
                    JobType = candidate.Type,
                    ScheduledAtUtc = candidate.ScheduledAtUtc,
                    Status = ScheduledAutomationJobStatus.Pending,
                    DeduplicationKey = key,
                    PayloadJson = JsonSerializer.Serialize(new ReservationAutomationJobPayload(candidate.Sequence), JsonOptions),
                    CreatedAt = DateTime.UtcNow
                }, ct);
            }

            await _unitOfWork.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessDueJobsAsync(CancellationToken ct)
    {
        var utcNow = DateTime.UtcNow;
        var due = await _unitOfWork.ScheduledAutomationJobs.GetDueAsync(utcNow, 50, ct);
        foreach (var job in due)
        {
            try
            {
                job.Status = ScheduledAutomationJobStatus.Locked;
                job.LockedUntilUtc = utcNow.Add(LockDuration);
                job.Attempts++;
                job.UpdatedAt = utcNow;
                await _unitOfWork.ScheduledAutomationJobs.UpdateAsync(job, ct);
                await _unitOfWork.SaveChangesAsync(ct);

                await SendJobAsync(job, ct);
            }
            catch (Exception ex)
            {
                job.Status = job.Attempts >= 3
                    ? ScheduledAutomationJobStatus.Failed
                    : ScheduledAutomationJobStatus.Pending;
                job.LockedUntilUtc = null;
                job.LastError = ex.Message;
                job.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.ScheduledAutomationJobs.UpdateAsync(job, ct);
                await _unitOfWork.SaveChangesAsync(ct);

                _logger.LogError(ex, "ReservationAutomationProcess: error procesando JobId={JobId}", job.ScheduledAutomationJobId);
            }
        }
    }

    private async Task SendJobAsync(ScheduledAutomationJob job, CancellationToken ct)
    {
        var reservation = job.Reservation;
        if (reservation.Status != ReservationStatus.Confirmed || !reservation.ReservationDateTime.HasValue)
        {
            await MarkSkippedAsync(job, "Reservation is no longer confirmed.", ct);
            return;
        }

        if (job.CreatedAt > job.ScheduledAtUtc)
        {
            await MarkSkippedAsync(job, "Job was created after its scheduled time.", ct);
            return;
        }

        var phone = reservation.CustomerPhoneSnapshot?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            await MarkSkippedAsync(job, "Reservation has no customer phone.", ct);
            return;
        }

        var payload = JsonSerializer.Deserialize<ReservationAutomationJobPayload>(job.PayloadJson, JsonOptions);
        if (payload is null || string.IsNullOrWhiteSpace(payload.SequenceName))
        {
            await MarkSkippedAsync(job, "Job payload has no sequenceName.", ct);
            return;
        }

        var config = await _configProvider.GetConfigAsync(job.AgentId, ct);
        var messages = await _sequenceResolver.ResolveAsync(
            job.BusinessId,
            payload.SequenceName,
            config.MessageSequences,
            new MessageSequenceContext
            {
                Reservation = reservation,
                Custom = new Dictionary<string, string>
                {
                    ["reservation_id"] = reservation.ReservationId.ToString("D"),
                    ["job_id"] = job.ScheduledAutomationJobId.ToString("D")
                }
            },
            ct);

        if (messages.Count == 0)
        {
            await MarkSkippedAsync(job, $"Sequence '{payload.SequenceName}' resolved empty.", ct);
            return;
        }

        await _dispatcher.SendAllAsync(job.BusinessId, phone, messages, reservation.ConversationId, ct, throwOnFailure: true);
        job.Status = ScheduledAutomationJobStatus.Sent;
        job.SentAtUtc = DateTime.UtcNow;
        job.LockedUntilUtc = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.ScheduledAutomationJobs.UpdateAsync(job, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task MarkSkippedAsync(ScheduledAutomationJob job, string reason, CancellationToken ct)
    {
        job.Status = ScheduledAutomationJobStatus.Skipped;
        job.LastError = reason;
        job.LockedUntilUtc = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.ScheduledAutomationJobs.UpdateAsync(job, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static IEnumerable<(ScheduledAutomationJobType Type, ReservationAutomationConfig Config)> EnumerateEnabledAutomations(
        AgentConfig config)
    {
        if (config.ReservationAutomations.Confirmation is { Enabled: true } confirmation &&
            !string.IsNullOrWhiteSpace(confirmation.SendMessageSequence))
        {
            yield return (ScheduledAutomationJobType.ReservationConfirmation, confirmation);
        }

        if (config.ReservationAutomations.Reminder is { Enabled: true } reminder &&
            !string.IsNullOrWhiteSpace(reminder.SendMessageSequence))
        {
            yield return (ScheduledAutomationJobType.ReservationReminder, reminder);
        }
    }

    private static DateTime? TryCalculateScheduledAtUtc(
        DateTime reservationLocal,
        ReservationAutomationTrigger trigger,
        TimeZoneInfo timeZone)
    {
        var type = trigger.Type?.Trim();
        DateTime scheduledLocal;

        if (string.Equals(type, "fixedLocalTime", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(trigger.Time) || !TimeOnly.TryParse(trigger.Time, out var time))
                return null;

            var date = DateOnly.FromDateTime(reservationLocal).AddDays(-Math.Max(0, trigger.DaysBefore));
            scheduledLocal = date.ToDateTime(time);
        }
        else
        {
            var hours = trigger.HoursBefore.GetValueOrDefault();
            if (hours < 0)
                return null;

            scheduledLocal = reservationLocal.AddHours(-hours);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(scheduledLocal, DateTimeKind.Unspecified), timeZone);
    }

    private static string BuildDeduplicationKey(
        Guid businessId,
        Reservation reservation,
        ScheduledAutomationJobType type,
        string sequenceName)
    {
        var stamp = reservation.ReservationDateTime?.ToString("yyyyMMddHHmm") ?? "no-date";
        return $"{businessId:N}:{reservation.ReservationId:N}:{type}:{stamp}:{sequenceName}".ToLowerInvariant();
    }

    private sealed record ReservationAutomationJobPayload(string SequenceName);
}
