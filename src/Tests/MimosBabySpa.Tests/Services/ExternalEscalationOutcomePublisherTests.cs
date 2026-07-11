using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class ExternalEscalationOutcomePublisherTests
{
    private readonly Guid _businessId = Guid.NewGuid();
    private readonly Guid _sourceAgentId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();
    private readonly Mock<IExternalEscalationAttemptRepository> _attempts = new();
    private readonly Mock<IExternalEscalationOutcomeDeliveryRepository> _deliveries = new();
    private readonly Mock<IAgentConfigProvider> _configProvider = new();
    private readonly Mock<IEventNotificationDispatcher> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public ExternalEscalationOutcomePublisherTests()
    {
        _unitOfWork.SetupGet(x => x.ExternalEscalationAttempts).Returns(_attempts.Object);
        _unitOfWork.SetupGet(x => x.ExternalEscalationOutcomeDeliveries).Returns(_deliveries.Object);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task EnqueueAsync_CreatesOneDurableDeliveryPerAttemptAndOutcome()
    {
        ExternalEscalationOutcomeDelivery? added = null;
        _deliveries.Setup(x => x.GetByAttemptAndOutcomeAsync(_attemptId, "accepted", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalEscalationOutcomeDelivery?)null);
        _deliveries.Setup(x => x.AddAsync(It.IsAny<ExternalEscalationOutcomeDelivery>(), It.IsAny<CancellationToken>()))
            .Callback<ExternalEscalationOutcomeDelivery, CancellationToken>((delivery, _) => added = delivery)
            .ReturnsAsync((ExternalEscalationOutcomeDelivery delivery, CancellationToken _) => delivery);

        var id = await CreatePublisher().EnqueueAsync(
            _businessId,
            _attemptId,
            " accepted ",
            new Dictionary<string, string> { ["order_number"] = "ORD-1" });

        id.Should().NotBeEmpty();
        added.Should().NotBeNull();
        added!.BusinessId.Should().Be(_businessId);
        added.ExternalEscalationAttemptId.Should().Be(_attemptId);
        added.OutcomeKey.Should().Be("accepted");
        added.PayloadJson.Should().Contain("ORD-1");
        added.PublishedAt.Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_SendsConfiguredOutcomeAndMarksDeliveryPublished()
    {
        var attempt = CreateAttempt();
        var delivery = CreateDelivery("accepted", """{"driver":"Luis"}""");
        SetupDelivery(delivery);
        SetupAttemptAndConfig(attempt);

        var published = await CreatePublisher().PublishAsync(delivery.ExternalEscalationOutcomeDeliveryId);

        published.Should().BeTrue();
        delivery.PublishedAt.Should().NotBeNull();
        delivery.PublishAttempts.Should().Be(1);
        delivery.LastError.Should().BeNull();
        _notifications.Verify(n => n.SendEventAsync(
            _businessId,
            It.IsAny<AgentConfig>(),
            "delivery_confirmed",
            It.Is<IReadOnlyDictionary<string, string>>(p =>
                p["outcome_key"] == "accepted" &&
                p["driver"] == "Luis" &&
                p["external_interaction_id"] == _attemptId.ToString() &&
                p["external_outcome_delivery_id"] == delivery.ExternalEscalationOutcomeDeliveryId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_WhenNotificationFails_RemainsPendingForRetry()
    {
        var delivery = CreateDelivery("timed_out", "{}");
        SetupDelivery(delivery);
        SetupAttemptAndConfig(CreateAttempt());
        _notifications.Setup(n => n.SendEventAsync(
                It.IsAny<Guid>(), It.IsAny<AgentConfig>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("WhatsApp unavailable"));

        var published = await CreatePublisher().PublishAsync(delivery.ExternalEscalationOutcomeDeliveryId);

        published.Should().BeFalse();
        delivery.PublishedAt.Should().BeNull();
        delivery.PublishAttempts.Should().Be(1);
        delivery.LastError.Should().Be("WhatsApp unavailable");
        delivery.NextAttemptAt.Should().BeAfter(delivery.LastAttemptAt!.Value);
    }

    [Fact]
    public async Task PublishPendingAsync_RetriesPendingDeliveryAndDoesNotRepublishCompletedOne()
    {
        var pending = CreateDelivery("declined", "{}");
        var completed = CreateDelivery("accepted", "{}");
        completed.PublishedAt = DateTime.UtcNow;
        _deliveries.Setup(x => x.GetPendingAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([pending]);
        SetupDelivery(pending);
        SetupAttemptAndConfig(CreateAttempt());

        var count = await CreatePublisher().PublishPendingAsync();

        count.Should().Be(1);
        pending.PublishedAt.Should().NotBeNull();
        _notifications.Verify(n => n.SendEventAsync(
            It.IsAny<Guid>(), It.IsAny<AgentConfig>(), "delivery_unavailable",
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private ExternalEscalationOutcomePublisher CreatePublisher() => new(
        _unitOfWork.Object,
        _configProvider.Object,
        _notifications.Object,
        NullLogger<ExternalEscalationOutcomePublisher>.Instance);

    private void SetupDelivery(ExternalEscalationOutcomeDelivery delivery) =>
        _deliveries.Setup(x => x.GetByIdAsync(delivery.ExternalEscalationOutcomeDeliveryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(delivery);

    private void SetupAttemptAndConfig(ExternalEscalationAttempt attempt)
    {
        _attempts.Setup(x => x.GetByIdAsync(_attemptId, It.IsAny<CancellationToken>())).ReturnsAsync(attempt);
        _configProvider.Setup(x => x.GetConfigAsync(_sourceAgentId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateConfig());
    }

    private AgentConfig CreateConfig() => new()
    {
        AgentId = _sourceAgentId,
        BusinessId = _businessId,
        Escalations = new EscalationDefinitions
        {
            External = new ExternalEscalationDefinitions
            {
                Enabled = true,
                Events = new Dictionary<string, ExternalEscalationEventDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["order_created"] = new()
                    {
                        Enabled = true,
                        Tool = "create_order",
                        OutcomeEvents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["requested"] = "delivery_requested",
                            ["accepted"] = "delivery_confirmed",
                            ["declined"] = "delivery_unavailable",
                            ["timed_out"] = "delivery_unavailable"
                        }
                    }
                }
            }
        }
    };

    private ExternalEscalationOutcomeDelivery CreateDelivery(string outcomeKey, string payloadJson) => new()
    {
        ExternalEscalationOutcomeDeliveryId = Guid.NewGuid(),
        BusinessId = _businessId,
        ExternalEscalationAttemptId = _attemptId,
        OutcomeKey = outcomeKey,
        PayloadJson = payloadJson,
        CreatedAt = DateTime.UtcNow.AddMinutes(-1),
        NextAttemptAt = DateTime.UtcNow.AddSeconds(-1)
    };

    private ExternalEscalationAttempt CreateAttempt() => new()
    {
        ExternalEscalationAttemptId = _attemptId,
        BusinessId = _businessId,
        SourceAgentId = _sourceAgentId,
        EventName = "order_created",
        TargetType = "order",
        TargetId = Guid.NewGuid(),
        ContactKey = "domicilio",
        ContactNameSnapshot = "Domicilio",
        ContactRoleSnapshot = "domicilio",
        ContactPhoneSnapshot = "573001112233",
        ContactTypeSnapshot = "domicilio",
        AttemptCode = "PED-12345",
        PickupAddressSnapshot = "Calle 10 #20-30",
        CustomPayloadJson = """{"order_number":"ORD-9001"}""",
        Status = ExternalEscalationAttemptStatus.Pending,
        EscalatedAt = DateTime.UtcNow.AddMinutes(-1),
        ExpiresAt = DateTime.UtcNow.AddMinutes(10)
    };
}
