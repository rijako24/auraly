using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ManageReservationToolTests
{
    [Fact]
    public async Task PreviewChange_DateTimeChange_AppliesImmediately()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation();
        fx.Resolve(reservation);
        fx.Reservations.Setup(r => r.UpdateReservationAsync(
                It.Is<UpdateReservationChangeRequest>(req => req.ReservationId == reservation.ReservationId && req.Apply && req.Time == new TimeOnly(14, 0)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fx.ChangeResult(reservation.ReservationId, applied: true));

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"preview_change","time":"14:00"}"""), fx.Context);

        result.Should().Contain("\"ok\":true");
        result.Should().Contain("\"applied\":true");
    }

    [Fact]
    public async Task ApplyChange_DateTimeChange_DoesNotRequireSecondConfirmation()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation();
        fx.Resolve(reservation);
        fx.Reservations.Setup(r => r.UpdateReservationAsync(
                It.Is<UpdateReservationChangeRequest>(req => req.ReservationId == reservation.ReservationId && req.Apply && req.Time == new TimeOnly(14, 0)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fx.ChangeResult(reservation.ReservationId, applied: true));

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"apply_change","time":"14:00"}"""), fx.Context);

        result.Should().Contain("\"ok\":true");
        result.Should().Contain("\"applied\":true");
    }

    [Fact]
    public async Task ApplyChange_DateAndTimeChange_AppliesConfiguredAutomaticFieldsImmediately()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation();
        fx.Resolve(reservation);
        fx.Reservations.Setup(r => r.UpdateReservationAsync(
                It.Is<UpdateReservationChangeRequest>(req =>
                    req.ReservationId == reservation.ReservationId
                    && req.Apply
                    && req.Date == new DateOnly(2026, 7, 10)
                    && req.Time == new TimeOnly(14, 0)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fx.ChangeResult(reservation.ReservationId, applied: true));

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"apply_change","date":"2026-07-10","time":"14:00"}"""), fx.Context);

        result.Should().Contain("\"ok\":true");
        result.Should().Contain("\"applied\":true");
        fx.Reservations.Verify(r => r.UpdateReservationAsync(
            It.Is<UpdateReservationChangeRequest>(req => req.Apply),
            It.IsAny<CancellationToken>()), Times.Once);
        fx.EscalationNotifier.Verify(n => n.NotifyAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<EscalationNotification>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyChange_AppliesAfterConfirmation()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation();
        fx.Resolve(reservation);
        fx.Reservations.Setup(r => r.UpdateReservationAsync(
                It.Is<UpdateReservationChangeRequest>(req => req.Apply && req.Time == new TimeOnly(14, 0)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fx.ChangeResult(reservation.ReservationId, applied: true));

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"apply_change","time":"14:00","customer_confirmed":true}"""), fx.Context);

        result.Should().Contain("\"ok\":true");
        result.Should().Contain("\"applied\":true");
        fx.Context.ManageableReservations.Should().ContainSingle(r => r.ReservationId == reservation.ReservationId);
    }
    [Fact]
    public async Task ServiceChange_PutsReservationOnHoldAndEscalates()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation(customerConfirmed: true);
        fx.Resolve(reservation);

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"preview_change","service":"Corte premium de adulto"}"""), fx.Context);

        result.Should().Contain("\"ok\":true");
        result.Should().Contain("OnHold");
        result.Should().Contain("escalated_to_human");
        result.Should().Contain("reservation_change_requires_human:service");
        result.Should().Contain("human_handoff");
        reservation.Status.Should().Be(ReservationStatus.OnHold);
        reservation.CustomerConfirmed.Should().BeFalse();
        fx.ReservationRepository.Verify(r => r.UpdateAsync(reservation), Times.Once);
        fx.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fx.EscalationNotifier.Verify(n => n.NotifyAsync(
            fx.BusinessId,
            It.IsAny<IReadOnlyList<string>>(),
            It.Is<EscalationNotification>(notification => notification.Reason == "reservation_change_requires_human:service"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddOnsChange_PutsReservationOnHoldAndEscalates()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation(customerConfirmed: true);
        fx.Resolve(reservation);

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"preview_change","add_ons":"Barba premium"}"""), fx.Context);

        result.Should().Contain("\"ok\":true");
        result.Should().Contain("OnHold");
        result.Should().Contain("escalated_to_human");
        result.Should().Contain("reservation_change_requires_human:add_ons");
        result.Should().Contain("human_handoff");
        reservation.Status.Should().Be(ReservationStatus.OnHold);
        reservation.CustomerConfirmed.Should().BeFalse();
        fx.ReservationRepository.Verify(r => r.UpdateAsync(reservation), Times.Once);
        fx.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fx.EscalationNotifier.Verify(n => n.NotifyAsync(
            fx.BusinessId,
            It.IsAny<IReadOnlyList<string>>(),
            It.Is<EscalationNotification>(notification => notification.Reason == "reservation_change_requires_human:add_ons"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestReschedule_WithoutTargetSlot_StoresResponseWithoutPuttingReservationOnHold()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation(customerConfirmed: true);
        fx.Resolve(reservation);
        ReservationAttendanceResponse? saved = null;
        fx.AttendanceResponses.Setup(r => r.GetLatestByReservationAsync(fx.BusinessId, reservation.ReservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationAttendanceResponse?)null);
        fx.AttendanceResponses.Setup(r => r.AddAsync(It.IsAny<ReservationAttendanceResponse>(), It.IsAny<CancellationToken>()))
            .Callback<ReservationAttendanceResponse, CancellationToken>((response, _) => saved = response)
            .ReturnsAsync((ReservationAttendanceResponse response, CancellationToken _) => response);

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"request_reschedule"}"""), fx.Context);

        result.Should().Contain("reschedule_requested");
        result.Should().Contain("collect_reschedule_target");
        reservation.Status.Should().Be(ReservationStatus.Confirmed);
        reservation.CustomerConfirmed.Should().BeTrue();
        fx.ReservationRepository.Verify(r => r.UpdateAsync(It.IsAny<Reservation>()), Times.Never);
        saved.Should().NotBeNull();
        saved!.ResponseType.Should().Be(ReservationAttendanceResponseType.RescheduleRequested);
        fx.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmAttendance_FromButtonJob_UsesJobReservationAndStoresSourceJob()
    {
        var fx = new Fixture();
        var unrelated = fx.CreateReservation(customerConfirmed: false);
        var buttonReservation = fx.CreateReservation(customerConfirmed: false);
        var jobId = Guid.NewGuid();
        fx.Resolve(unrelated);
        fx.ScheduledJobs.Setup(j => j.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduledAutomationJob
            {
                ScheduledAutomationJobId = jobId,
                BusinessId = fx.BusinessId,
                ReservationId = buttonReservation.ReservationId,
                Reservation = buttonReservation,
                JobType = ScheduledAutomationJobType.ReservationConfirmation,
                ScheduledAtUtc = DateTime.UtcNow
            });
        ReservationAttendanceResponse? saved = null;
        fx.AttendanceResponses.Setup(r => r.GetLatestByReservationAsync(fx.BusinessId, buttonReservation.ReservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationAttendanceResponse?)null);
        fx.AttendanceResponses.Setup(r => r.AddAsync(It.IsAny<ReservationAttendanceResponse>(), It.IsAny<CancellationToken>()))
            .Callback<ReservationAttendanceResponse, CancellationToken>((response, _) => saved = response)
            .ReturnsAsync((ReservationAttendanceResponse response, CancellationToken _) => response);

        var result = await fx.Tool.ExecuteAsync(
            Json($$"""{"action":"confirm_attendance","customer_confirmed":true,"job_id":"{{jobId:D}}"}"""),
            fx.Context);

        result.Should().Contain("attendance_confirmed");
        buttonReservation.CustomerConfirmed.Should().BeTrue();
        unrelated.CustomerConfirmed.Should().BeFalse();
        saved.Should().NotBeNull();
        saved!.ReservationId.Should().Be(buttonReservation.ReservationId);
        saved.SourceJobId.Should().Be(jobId);
    }

    [Fact]
    public async Task Cancel_RequiresConfirmationAndSuspendsReservation()
    {
        var fx = new Fixture();
        var reservation = fx.CreateReservation();
        fx.Resolve(reservation);
        fx.Reservations.Setup(r => r.SuspendAsync(reservation.ReservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var missingConfirmation = await fx.Tool.ExecuteAsync(Json("""{"action":"cancel"}"""), fx.Context);
        var confirmed = await fx.Tool.ExecuteAsync(Json("""{"action":"cancel","customer_confirmed":true}"""), fx.Context);

        missingConfirmation.Should().Contain("confirmation_required");
        confirmed.Should().Contain("suspended");
        fx.Reservations.Verify(r => r.SuspendAsync(reservation.ReservationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompletePaidReschedule_CreatesReservationAndLinksPayment()
    {
        var fx = new Fixture();
        var serviceId = Guid.NewGuid();
        var payment = fx.PendingReschedulePayment(serviceId);
        var newReservationId = Guid.NewGuid();
        fx.PaymentLifecycle.Setup(p => p.GetPendingReschedulingByConversationAsync(fx.ConversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        fx.Services.Setup(s => s.GetByIdAsync(serviceId))
            .ReturnsAsync(new Service { ServiceId = serviceId, ServiceName = "Corte basico de adulto" });
        fx.Availability.Setup(a => a.CheckAvailabilityAsync(fx.BusinessId, "Corte basico de adulto", new DateTime(2026, 7, 8), new TimeSpan(14, 0, 0), It.IsAny<AvailabilityParams?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailabilityResult { IsAvailable = true, ResponseMessage = "Disponible" });
        fx.Reservations.Setup(r => r.CreateFromIntentSnapshotAsync(fx.BusinessId, fx.ConversationId, It.IsAny<ReservationIntentSnapshot>(), new DateTime(2026, 7, 8, 14, 0, 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateReservationResponse(newReservationId, "Corte basico de adulto", "Luis Petit", new DateOnly(2026, 7, 8), new TimeOnly(14, 0), 30, []));

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"complete_paid_reschedule","date":"2026-07-08","time":"14:00"}"""), fx.Context);

        result.Should().Contain("reservation_created");
        result.Should().Contain(newReservationId.ToString("D"));
        fx.PaymentLifecycle.Verify(p => p.LinkReservationAsync(payment, newReservationId, It.IsAny<CancellationToken>()), Times.Once);
        fx.Context.NotificationContexts.Should().ContainKey("reservation_created");
    }

    [Fact]
    public async Task CompletePaidReschedule_WhenSlotUnavailable_DoesNotCreateReservation()
    {
        var fx = new Fixture();
        var serviceId = Guid.NewGuid();
        var payment = fx.PendingReschedulePayment(serviceId);
        fx.PaymentLifecycle.Setup(p => p.GetPendingReschedulingByConversationAsync(fx.ConversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        fx.Services.Setup(s => s.GetByIdAsync(serviceId))
            .ReturnsAsync(new Service { ServiceId = serviceId, ServiceName = "Corte basico de adulto" });
        fx.Availability.Setup(a => a.CheckAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan?>(), It.IsAny<AvailabilityParams?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailabilityResult
            {
                IsAvailable = false,
                ResponseMessage = "No disponible",
                AvailableOptions = [new AvailabilityOption("15:00", "15:30")]
            });

        var result = await fx.Tool.ExecuteAsync(Json("""{"action":"complete_paid_reschedule","date":"2026-07-08","time":"14:00"}"""), fx.Context);

        result.Should().Contain("slot_unavailable");
        result.Should().Contain("offer_alternative_slots");
        result.Should().Contain("15:00");
        fx.Reservations.Verify(r => r.CreateFromIntentSnapshotAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ReservationIntentSnapshot>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        fx.PaymentLifecycle.Verify(p => p.LinkReservationAsync(It.IsAny<PaymentTransaction>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private sealed class Fixture
    {
        public Guid BusinessId { get; } = Guid.NewGuid();
        public Guid ConversationId { get; } = Guid.NewGuid();
        public Mock<IReservationService> Reservations { get; } = new();
        public Mock<ICustomerReservationResolver> Resolver { get; } = new();
        public Mock<IPaymentLifecycleService> PaymentLifecycle { get; } = new();
        public Mock<IAvailabilityService> Availability { get; } = new();
        public Mock<ISchedulingPolicyProvider> SchedulingPolicy { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IConversationVerificationService> Verifications { get; } = new();
        public Mock<IEscalationNotifier> EscalationNotifier { get; } = new();
        public Mock<IReservationRepository> ReservationRepository { get; } = new();
        public Mock<IReservationAttendanceResponseRepository> AttendanceResponses { get; } = new();
        public Mock<IScheduledAutomationJobRepository> ScheduledJobs { get; } = new();
        public Mock<IServiceRepository> Services { get; } = new();
        public Mock<IPaymentTransactionRepository> PaymentTransactions { get; } = new();
        public AgentToolContext Context { get; }
        public ManageReservationTool Tool { get; }

        public Fixture()
        {
            Context = new AgentToolContext
            {
                BusinessId = BusinessId,
                ConversationId = ConversationId,
                ConversationState = new MimosBabySpa.Domain.Models.ConversationState { ConversationId = ConversationId, BusinessId = BusinessId },
                ChannelPhone = "+573001112233",
                LatestUserMessage = "Quiero cambiar el servicio",
                EscalationContacts = ["+573009998888"],
                Config = new AgentConfig
                {
                    ReservationManagement = new ReservationManagementDefinitions
                    {
                        AutomaticChangeFields = ["date", "time"],
                        EscalateChangeFields = ["service", "add_ons"],
                        EscalationReasonCode = "reservation_change_requires_human"
                    }
                },
                Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["service"] = "Corte basico de adulto"
                }
            };
            UnitOfWork.SetupGet(u => u.Reservations).Returns(ReservationRepository.Object);
            UnitOfWork.SetupGet(u => u.ReservationAttendanceResponses).Returns(AttendanceResponses.Object);
            UnitOfWork.SetupGet(u => u.ScheduledAutomationJobs).Returns(ScheduledJobs.Object);
            UnitOfWork.SetupGet(u => u.Services).Returns(Services.Object);
            UnitOfWork.SetupGet(u => u.PaymentTransactions).Returns(PaymentTransactions.Object);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            ReservationRepository.Setup(r => r.UpdateAsync(It.IsAny<Reservation>()))
                .ReturnsAsync((Reservation reservation) => reservation);
            SchedulingPolicy.Setup(p => p.GetAsync(BusinessId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AvailabilityParams());

            Tool = new ManageReservationTool(
                Reservations.Object,
                Resolver.Object,
                PaymentLifecycle.Object,
                Availability.Object,
                SchedulingPolicy.Object,
                UnitOfWork.Object,
                Verifications.Object,
                EscalationNotifier.Object);
        }

        public Reservation CreateReservation(bool customerConfirmed = true) => new()
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = BusinessId,
            ConversationId = ConversationId,
            ReservationDateTime = new DateTime(2026, 7, 8, 11, 0, 0),
            Status = ReservationStatus.Confirmed,
            CustomerConfirmed = customerConfirmed,
            CustomerPhoneSnapshot = "+573001112233"
        };

        public void Resolve(Reservation reservation) => Resolver
            .Setup(r => r.ResolveAsync(It.IsAny<AgentToolContext>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReservationResolveResult.Ok(reservation));

        public UpdateReservationChangeResult ChangeResult(Guid reservationId, bool applied) => new(
            true,
            null,
            null,
            null,
            reservationId,
            "Corte basico de adulto",
            new DateOnly(2026, 7, 8),
            new TimeOnly(14, 0),
            "Luis Petit",
            30,
            [],
            30000m,
            "paid",
            applied);

        public PaymentTransaction PendingReschedulePayment(Guid serviceId) => new()
        {
            PaymentTransactionId = Guid.NewGuid(),
            BusinessId = BusinessId,
            ConversationId = ConversationId,
            Status = PaymentTransactionStatus.Confirmed,
            RequiresRescheduling = true,
            CheckoutSnapshotJson = $$"""
            {
              "service_id": "{{serviceId:D}}",
              "service_name": "Corte basico de adulto",
              "duration_minutes": 30,
              "payer_name": "Cliente Test",
              "payment_phone": "+573001112233",
              "reservation_date": "2026-07-08",
              "reservation_time": "11:00"
            }
            """
        };
    }
}
