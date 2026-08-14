using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Application.Time;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class ReservationAutomationProcessTests
{
    [Fact]
    public async Task RunAsync_WhenRelativeConfirmationTimeAlreadyPassed_DoesNotCreateImmediateJob()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var reservation = CreateReservation(businessId, new DateTime(2026, 7, 5, 10, 0, 0));
        var (process, scheduledJobs, dispatcher) = CreateProcess(businessId, agentId, now, [reservation]);

        await process.RunAsync();

        scheduledJobs.Verify(j => j.AddAsync(It.IsAny<ScheduledAutomationJob>(), It.IsAny<CancellationToken>()), Times.Never);
        dispatcher.Verify(d => d.SendAllAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<OutboundMessage>>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenRelativeConfirmationTimeIsFuture_CreatesPendingJob()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var reservation = CreateReservation(businessId, new DateTime(2026, 7, 6, 13, 0, 0));
        ScheduledAutomationJob? created = null;
        var (process, scheduledJobs, _) = CreateProcess(
            businessId,
            agentId,
            now,
            [reservation],
            job => created = job);

        await process.RunAsync();

        created.Should().NotBeNull();
        created!.BusinessId.Should().Be(businessId);
        created.ReservationId.Should().Be(reservation.ReservationId);
        created.AgentId.Should().Be(agentId);
        created.JobType.Should().Be(ScheduledAutomationJobType.ReservationConfirmation);
        created.Status.Should().Be(ScheduledAutomationJobStatus.Pending);
        created.ScheduledAtUtc.Should().Be(new DateTime(2026, 7, 5, 13, 0, 0, DateTimeKind.Utc));
        scheduledJobs.Verify(j => j.AddAsync(It.IsAny<ScheduledAutomationJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenExistingJobWasCreatedAfterSchedule_SkipsWithoutSending()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var reservation = CreateReservation(businessId, new DateTime(2026, 7, 5, 10, 0, 0));
        var staleJob = new ScheduledAutomationJob
        {
            ScheduledAutomationJobId = Guid.NewGuid(),
            BusinessId = businessId,
            ReservationId = reservation.ReservationId,
            AgentId = agentId,
            JobType = ScheduledAutomationJobType.ReservationConfirmation,
            ScheduledAtUtc = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc),
            Status = ScheduledAutomationJobStatus.Pending,
            PayloadJson = "{\"sequenceName\":\"reservation_confirmation_request\"}",
            Reservation = reservation
        };
        var (process, _, dispatcher) = CreateProcess(
            businessId,
            agentId,
            now,
            [],
            dueJobs: [staleJob]);

        await process.RunAsync();

        staleJob.Status.Should().Be(ScheduledAutomationJobStatus.Skipped);
        staleJob.LastError.Should().Be("Job was created after its scheduled time.");
        dispatcher.Verify(d => d.SendAllAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<OutboundMessage>>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenDueConfirmationIsInsideRelativeWindow_SkipsWithoutSending()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var utcNow = UtcNowTruncatedToMinute();
        var reservation = CreateReservation(businessId, utcNow.AddHours(23));
        var staleJob = new ScheduledAutomationJob
        {
            ScheduledAutomationJobId = Guid.NewGuid(),
            BusinessId = businessId,
            ReservationId = reservation.ReservationId,
            AgentId = agentId,
            JobType = ScheduledAutomationJobType.ReservationConfirmation,
            ScheduledAtUtc = utcNow.AddHours(-1),
            CreatedAt = utcNow.AddHours(-2),
            Status = ScheduledAutomationJobStatus.Pending,
            PayloadJson = "{\"sequenceName\":\"reservation_confirmation_request\"}",
            Reservation = reservation
        };
        var (process, _, dispatcher) = CreateProcess(
            businessId,
            agentId,
            new DateTimeOffset(utcNow, TimeSpan.Zero),
            [],
            dueJobs: [staleJob]);

        await process.RunAsync();

        staleJob.Status.Should().Be(ScheduledAutomationJobStatus.Skipped);
        staleJob.LastError.Should().Be("Configured automation time has already passed.");
        dispatcher.Verify(d => d.SendAllAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<OutboundMessage>>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenDueReminderReservationTimeAlreadyPassed_SkipsWithoutSending()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var utcNow = UtcNowTruncatedToMinute();
        var reservation = CreateReservation(businessId, utcNow.AddMinutes(-30));
        var staleJob = new ScheduledAutomationJob
        {
            ScheduledAutomationJobId = Guid.NewGuid(),
            BusinessId = businessId,
            ReservationId = reservation.ReservationId,
            AgentId = agentId,
            JobType = ScheduledAutomationJobType.ReservationReminder,
            ScheduledAtUtc = utcNow.AddHours(-1),
            CreatedAt = utcNow.AddHours(-2),
            Status = ScheduledAutomationJobStatus.Pending,
            PayloadJson = "{\"sequenceName\":\"reservation_reminder\"}",
            Reservation = reservation
        };
        var (process, _, dispatcher) = CreateProcess(
            businessId,
            agentId,
            new DateTimeOffset(utcNow, TimeSpan.Zero),
            [],
            dueJobs: [staleJob],
            reservationAutomations: CreateAutomations(reminder: CreateReminderAutomation(utcNow.ToString("HH:mm"))));

        await process.RunAsync();

        staleJob.Status.Should().Be(ScheduledAutomationJobStatus.Skipped);
        staleJob.LastError.Should().Be("Reservation time has already passed.");
        dispatcher.Verify(d => d.SendAllAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<OutboundMessage>>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenConfirmationConfiguredTimeHasNotArrived_SkipsWithoutSending()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var reservation = CreateReservation(businessId, now.UtcDateTime.AddHours(25));
        var job = CreateDueJob(businessId, agentId, reservation, ScheduledAutomationJobType.ReservationConfirmation, now.UtcDateTime.AddHours(1));
        var (process, _, dispatcher) = CreateProcess(businessId, agentId, now, [], dueJobs: [job]);

        await process.RunAsync();

        job.Status.Should().Be(ScheduledAutomationJobStatus.Pending);
        job.ScheduledAtUtc.Should().Be(now.UtcDateTime.AddHours(1));
        job.LastError.Should().Be("Configured automation time has not arrived.");
        VerifyNoMessages(dispatcher);
    }

    [Fact]
    public async Task RunAsync_WhenConfirmationConfiguredTimeIsNow_SendsMessage()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var reservation = CreateReservation(businessId, now.UtcDateTime.AddHours(24));
        var job = CreateDueJob(businessId, agentId, reservation, ScheduledAutomationJobType.ReservationConfirmation, now.UtcDateTime);
        var (process, _, dispatcher) = CreateProcess(businessId, agentId, now, [], dueJobs: [job]);

        await process.RunAsync();

        job.Status.Should().Be(ScheduledAutomationJobStatus.Sent);
        dispatcher.Verify(d => d.SendAllAsync(
            businessId,
            reservation.CustomerPhoneSnapshot!,
            It.Is<IReadOnlyList<OutboundMessage>>(messages => messages.Count == 1),
            reservation.ConversationId,
            It.IsAny<CancellationToken>(),
            true), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenReminderConfiguredTimeHasNotArrived_SkipsWithoutSending()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 4, 7, 59, 0, TimeSpan.Zero);
        var reservation = CreateReservation(businessId, new DateTime(2026, 7, 4, 10, 0, 0));
        var job = CreateDueJob(businessId, agentId, reservation, ScheduledAutomationJobType.ReservationReminder, new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc));
        var (process, _, dispatcher) = CreateProcess(
            businessId,
            agentId,
            now,
            [],
            dueJobs: [job],
            reservationAutomations: CreateAutomations(reminder: CreateReminderAutomation("08:00")));

        await process.RunAsync();

        job.Status.Should().Be(ScheduledAutomationJobStatus.Pending);
        job.ScheduledAtUtc.Should().Be(new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc));
        job.LastError.Should().Be("Configured automation time has not arrived.");
        VerifyNoMessages(dispatcher);
    }

    [Fact]
    public async Task RunAsync_WhenReminderConfiguredTimeIsNow_SendsMessage()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 4, 8, 0, 0, TimeSpan.Zero);
        var reservation = CreateReservation(businessId, new DateTime(2026, 7, 4, 10, 0, 0));
        var job = CreateDueJob(businessId, agentId, reservation, ScheduledAutomationJobType.ReservationReminder, now.UtcDateTime);
        var (process, _, dispatcher) = CreateProcess(
            businessId,
            agentId,
            now,
            [],
            dueJobs: [job],
            reservationAutomations: CreateAutomations(reminder: CreateReminderAutomation("08:00")));

        await process.RunAsync();

        job.Status.Should().Be(ScheduledAutomationJobStatus.Sent);
        dispatcher.Verify(d => d.SendAllAsync(
            businessId,
            reservation.CustomerPhoneSnapshot!,
            It.Is<IReadOnlyList<OutboundMessage>>(messages => messages.Count == 1),
            reservation.ConversationId,
            It.IsAny<CancellationToken>(),
            true), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenReminderConfiguredTimeAlreadyPassed_SkipsWithoutSending()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 4, 8, 1, 0, TimeSpan.Zero);
        var reservation = CreateReservation(businessId, new DateTime(2026, 7, 4, 10, 0, 0));
        var job = CreateDueJob(businessId, agentId, reservation, ScheduledAutomationJobType.ReservationReminder, new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc));
        var (process, _, dispatcher) = CreateProcess(
            businessId,
            agentId,
            now,
            [],
            dueJobs: [job],
            reservationAutomations: CreateAutomations(reminder: CreateReminderAutomation("08:00")));

        await process.RunAsync();

        job.Status.Should().Be(ScheduledAutomationJobStatus.Skipped);
        job.LastError.Should().Be("Configured automation time has already passed.");
        VerifyNoMessages(dispatcher);
    }

    private static (
        ReservationAutomationProcess Process,
        Mock<IScheduledAutomationJobRepository> ScheduledJobs,
        Mock<IOutboundMessageDispatcher> Dispatcher) CreateProcess(
            Guid businessId,
            Guid agentId,
            DateTimeOffset now,
            IReadOnlyList<Reservation> reservations,
            Action<ScheduledAutomationJob>? onJobCreated = null,
            IReadOnlyList<ScheduledAutomationJob>? dueJobs = null,
            ReservationAutomationDefinitions? reservationAutomations = null)
    {
        var agents = new Mock<IAgentRepository>();
        agents.Setup(a => a.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Agent { AgentId = agentId, BusinessId = businessId, IsActive = true }]);

        var configProvider = new Mock<IAgentConfigProvider>();
        configProvider.Setup(p => p.GetConfigAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentConfig
            {
                AgentId = agentId,
                BusinessId = businessId,
                Name = "Test agent",
                ReservationAutomations = reservationAutomations ?? CreateAutomations()
            });

        var clock = new Mock<IBusinessClock>();
        clock.Setup(c => c.GetSnapshotAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessClockSnapshot(
                businessId,
                now,
                DateOnly.FromDateTime(now.DateTime),
                TimeZoneInfo.Utc));

        var reservationRepository = new Mock<IReservationRepository>();
        reservationRepository.Setup(r => r.GetUpcomingConfirmedByBusinessIdAsync(
                businessId,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservations);

        var scheduledJobs = new Mock<IScheduledAutomationJobRepository>();
        scheduledJobs.Setup(j => j.GetByDeduplicationKeysAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ScheduledAutomationJob>(StringComparer.OrdinalIgnoreCase));
        scheduledJobs.Setup(j => j.GetDueAsync(It.IsAny<DateTime>(), 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => dueJobs ?? []);
        scheduledJobs.Setup(j => j.AddAsync(It.IsAny<ScheduledAutomationJob>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledAutomationJob, CancellationToken>((job, _) => onJobCreated?.Invoke(job))
            .Returns((ScheduledAutomationJob job, CancellationToken _) => Task.FromResult(job));
        scheduledJobs.Setup(j => j.UpdateAsync(It.IsAny<ScheduledAutomationJob>(), It.IsAny<CancellationToken>()))
            .Returns((ScheduledAutomationJob job, CancellationToken _) => Task.FromResult(job));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Reservations).Returns(reservationRepository.Object);
        unitOfWork.SetupGet(u => u.ScheduledAutomationJobs).Returns(scheduledJobs.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var sequenceResolver = new Mock<IMessageSequenceResolver>();
        sequenceResolver.Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<MessageSequenceCatalog>(),
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OutboundMessage("ok", null)]);

        var dispatcher = new Mock<IOutboundMessageDispatcher>();
        dispatcher.Setup(d => d.SendAllAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<OutboundMessage>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var process = new ReservationAutomationProcess(
            unitOfWork.Object,
            agents.Object,
            configProvider.Object,
            clock.Object,
            sequenceResolver.Object,
            dispatcher.Object,
            NullLogger<ReservationAutomationProcess>.Instance);

        return (process, scheduledJobs, dispatcher);
    }

    private static ScheduledAutomationJob CreateDueJob(
        Guid businessId,
        Guid agentId,
        Reservation reservation,
        ScheduledAutomationJobType jobType,
        DateTime scheduledAtUtc) => new()
    {
        ScheduledAutomationJobId = Guid.NewGuid(),
        BusinessId = businessId,
        ReservationId = reservation.ReservationId,
        AgentId = agentId,
        JobType = jobType,
        ScheduledAtUtc = scheduledAtUtc,
        CreatedAt = scheduledAtUtc.AddHours(-1),
        Status = ScheduledAutomationJobStatus.Pending,
        PayloadJson = jobType == ScheduledAutomationJobType.ReservationReminder
            ? "{\"sequenceName\":\"reservation_reminder\"}"
            : "{\"sequenceName\":\"reservation_confirmation_request\"}",
        Reservation = reservation
    };

    private static ReservationAutomationDefinitions CreateAutomations(
        int confirmationHoursBefore = 24,
        ReservationAutomationConfig? reminder = null) => new()
    {
        Confirmation = new ReservationAutomationConfig
        {
            Enabled = true,
            Trigger = new ReservationAutomationTrigger
            {
                Type = "relative",
                HoursBefore = confirmationHoursBefore
            },
            SendMessageSequence = "reservation_confirmation_request"
        },
        Reminder = reminder
    };

    private static ReservationAutomationConfig CreateReminderAutomation(string time) => new()
    {
        Enabled = true,
        Trigger = new ReservationAutomationTrigger
        {
            Type = "fixedLocalTime",
            DaysBefore = 0,
            Time = time
        },
        SendMessageSequence = "reservation_reminder"
    };

    private static void VerifyNoMessages(Mock<IOutboundMessageDispatcher> dispatcher) =>
        dispatcher.Verify(d => d.SendAllAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<OutboundMessage>>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.Never);

    private static DateTime UtcNowTruncatedToMinute()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
    }

    private static Reservation CreateReservation(Guid businessId, DateTime reservationLocal) => new()
    {
        ReservationId = Guid.NewGuid(),
        BusinessId = businessId,
        ReservationDateTime = reservationLocal,
        Status = ReservationStatus.Confirmed,
        CustomerPhoneSnapshot = "+573001112233"
    };
}
