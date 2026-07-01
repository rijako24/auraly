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
    private readonly Mock<IAgentConfigProvider> _configProvider = new();
    private readonly Mock<IEventNotificationDispatcher> _notifications = new();

    [Fact]
    public async Task PublishAsync_SendsDeliveryConfirmedAsNormalNotification()
    {
        var attempt = CreateAttempt();
        var config = CreateConfig();
        SetupAttempt(attempt);
        SetupConfig(config);

        var publisher = CreatePublisher();

        await publisher.PublishAsync(
            _businessId,
            _attemptId,
            "accepted",
            new Dictionary<string, string> { ["driver"] = "Luis" });

        _notifications.Verify(n => n.SendEventAsync(
            _businessId,
            config,
            "delivery_confirmed",
            It.Is<IReadOnlyDictionary<string, string>>(p =>
                p["outcome_key"] == "accepted" &&
                p["driver"] == "Luis" &&
                p["external_interaction_id"] == _attemptId.ToString() &&
                p["attempt_code"] == "PED-12345"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_PreservesSpecificOutcomeKey()
    {
        var attempt = CreateAttempt();
        var config = CreateConfig();
        SetupAttempt(attempt);
        SetupConfig(config);

        var publisher = CreatePublisher();

        await publisher.PublishAsync(
            _businessId,
            _attemptId,
            "timed_out");

        _notifications.Verify(n => n.SendEventAsync(
            _businessId,
            config,
            "delivery_unavailable",
            It.Is<IReadOnlyDictionary<string, string>>(p => p["outcome_key"] == "timed_out"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_SendsDeliveryRequestedAsNormalNotification()
    {
        var attempt = CreateAttempt();
        var config = CreateConfig();
        SetupAttempt(attempt);
        SetupConfig(config);

        var publisher = CreatePublisher();

        await publisher.PublishAsync(_businessId, _attemptId, "requested");

        _notifications.Verify(n => n.SendEventAsync(
            _businessId,
            config,
            "delivery_requested",
            It.Is<IReadOnlyDictionary<string, string>>(p =>
                p["order_number"] == "ORD-9001" &&
                p["contact_phone"] == "573001112233"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private ExternalEscalationOutcomePublisher CreatePublisher()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.ExternalEscalationAttempts).Returns(_attempts.Object);

        return new ExternalEscalationOutcomePublisher(
            unitOfWork.Object,
            _configProvider.Object,
            _notifications.Object,
            NullLogger<ExternalEscalationOutcomePublisher>.Instance);
    }

    private void SetupAttempt(ExternalEscalationAttempt attempt) =>
        _attempts
            .Setup(r => r.GetByIdAsync(_attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);

    private void SetupConfig(AgentConfig config) =>
        _configProvider
            .Setup(p => p.GetConfigAsync(_sourceAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

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

