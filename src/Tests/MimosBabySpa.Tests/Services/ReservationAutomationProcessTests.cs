using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

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

    private static (
        ReservationAutomationProcess Process,
        Mock<IScheduledAutomationJobRepository> ScheduledJobs,
        Mock<IOutboundMessageDispatcher> Dispatcher) CreateProcess(
            Guid businessId,
            Guid agentId,
            DateTimeOffset now,
            IReadOnlyList<Reservation> reservations,
            Action<ScheduledAutomationJob>? onJobCreated = null,
            IReadOnlyList<ScheduledAutomationJob>? dueJobs = null)
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
                ReservationAutomations = new ReservationAutomationDefinitions
                {
                    Confirmation = new ReservationAutomationConfig
                    {
                        Enabled = true,
                        Trigger = new ReservationAutomationTrigger
                        {
                            Type = "relative",
                            HoursBefore = 24
                        },
                        SendMessageSequence = "reservation_confirmation_request"
                    }
                }
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
        var dispatcher = new Mock<IOutboundMessageDispatcher>();

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

    private static Reservation CreateReservation(Guid businessId, DateTime reservationLocal) => new()
    {
        ReservationId = Guid.NewGuid(),
        BusinessId = businessId,
        ReservationDateTime = reservationLocal,
        Status = ReservationStatus.Confirmed,
        CustomerPhoneSnapshot = "+573001112233"
    };
}
