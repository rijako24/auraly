using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ConfirmReservationAttendanceToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenButtonPayloadHasJobId_ConfirmsOnlyThatReservationEvenWithAnotherReservationInConversation()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var unrelatedReservation = CreateReservation(businessId, conversationId, DateTime.UtcNow.AddDays(1));
        var buttonReservation = CreateReservation(businessId, conversationId, DateTime.UtcNow.AddDays(2));
        var jobId = Guid.NewGuid();
        var sourceJob = new ScheduledAutomationJob
        {
            ScheduledAutomationJobId = jobId,
            BusinessId = businessId,
            ReservationId = buttonReservation.ReservationId,
            Reservation = buttonReservation,
            JobType = ScheduledAutomationJobType.ReservationConfirmation,
            ScheduledAtUtc = DateTime.UtcNow
        };

        ReservationAttendanceResponse? savedResponse = null;
        var scheduledJobs = new Mock<IScheduledAutomationJobRepository>();
        scheduledJobs.Setup(j => j.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceJob);

        var reservations = new Mock<IReservationRepository>();
        reservations.Setup(r => r.UpdateAsync(It.IsAny<Reservation>()))
            .ReturnsAsync((Reservation reservation) => reservation);

        var responses = new Mock<IReservationAttendanceResponseRepository>();
        responses.Setup(r => r.GetLatestByReservationAsync(businessId, buttonReservation.ReservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationAttendanceResponse?)null);
        responses.Setup(r => r.AddAsync(It.IsAny<ReservationAttendanceResponse>(), It.IsAny<CancellationToken>()))
            .Callback<ReservationAttendanceResponse, CancellationToken>((response, _) => savedResponse = response)
            .ReturnsAsync((ReservationAttendanceResponse response, CancellationToken _) => response);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.ScheduledAutomationJobs).Returns(scheduledJobs.Object);
        unitOfWork.SetupGet(u => u.Reservations).Returns(reservations.Object);
        unitOfWork.SetupGet(u => u.ReservationAttendanceResponses).Returns(responses.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var resolver = new Mock<ICustomerReservationResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<AgentToolContext>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReservationResolveResult.Ok(unrelatedReservation));

        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationId = conversationId,
            InteractivePayload = $"reservation_attendance:confirm:{jobId:D}",
            ManageableReservations = [unrelatedReservation, buttonReservation]
        };
        var arguments = JsonDocument.Parse("""{"customer_confirmed":true}""").RootElement;
        var tool = new ConfirmReservationAttendanceTool(unitOfWork.Object, resolver.Object);

        var result = await tool.ExecuteAsync(arguments, ctx);

        result.Should().Contain(buttonReservation.ReservationId.ToString("D"));
        buttonReservation.CustomerConfirmed.Should().BeTrue();
        unrelatedReservation.CustomerConfirmed.Should().BeFalse();
        savedResponse.Should().NotBeNull();
        savedResponse!.ReservationId.Should().Be(buttonReservation.ReservationId);
        savedResponse.SourceJobId.Should().Be(jobId);
        resolver.Verify(r => r.ResolveAsync(It.IsAny<AgentToolContext>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Reservation CreateReservation(Guid businessId, Guid conversationId, DateTime reservationDateTime) => new()
    {
        ReservationId = Guid.NewGuid(),
        BusinessId = businessId,
        ConversationId = conversationId,
        ReservationDateTime = reservationDateTime,
        Status = ReservationStatus.Confirmed,
        CustomerPhoneSnapshot = "+573001112233"
    };
}
