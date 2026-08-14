using System.Text.Json;
using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class ExternalEscalationServiceTests
{
    private readonly Guid _businessId = Guid.NewGuid();
    private readonly Guid _sourceAgentId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly FakeExternalEscalationAttemptRepository _attempts = new();
    private readonly FakeBusinessInboundContactRepository _contacts = new();
    private readonly Mock<IMessageSequenceResolver> _sequenceResolver = new();
    private readonly Mock<IWhatsAppService> _whatsApp = new();
    private readonly Mock<IExternalEscalationOutcomePublisher> _outcomes = new();

    public ExternalEscalationServiceTests()
    {
        _sequenceResolver
            .Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<MessageSequenceCatalog>(),
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OutboundMessage("Solicitud de domicilio", null)]);

        _whatsApp
            .Setup(w => w.SendTextMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _outcomes
            .Setup(o => o.EnqueueAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _outcomes
            .Setup(o => o.PublishAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

    }
    [Fact]
    public async Task Router_ResolvesOnlyActiveInboundContactWithMatchingBusinessAndAgent()
    {
        var inboundAgentId = Guid.NewGuid();
        var contact = CreateContact(Guid.NewGuid(), inboundAgentId, "operations", "+57 300 111 2233");
        _contacts.Items.Add(contact);
        var router = new BusinessInboundContactRouter(CreateUnitOfWork().Object);

        var route = await router.ResolveAsync(_businessId, "+57 300 111 2233");

        route.Should().NotBeNull();
        route!.AgentId.Should().Be(inboundAgentId);
        route.ContactKey.Should().Be(contact.Key);
        route.ContactPhone.Should().Be("573001112233");
    }

    [Fact]
    public async Task EscalateAsync_UsesConfiguredInboundContactAndPickupAddressFromEvent()
    {
        var contactId = Guid.NewGuid();
        var inboundAgentId = Guid.NewGuid();
        _contacts.Items.Add(CreateContact(contactId, inboundAgentId, "domicilio", "+57 304 205 2007"));
        var service = CreateService(CreateConfig(contactId));
        MessageSequenceContext? sequenceContext = null;
        _sequenceResolver
            .Setup(r => r.ResolveAsync(
                _businessId,
                "domicilio_request",
                It.IsAny<MessageSequenceCatalog>(),
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MessageSequenceCatalog, MessageSequenceContext, CancellationToken>((_, _, _, ctx, _) => sequenceContext = ctx)
            .ReturnsAsync([new OutboundMessage("Solicitud de domicilio", null)]);

        var result = await service.EscalateAsync(new ExternalEscalationRequest(
            _sourceAgentId,
            "order_created",
            _targetId,
            new Dictionary<string, string> { ["order_id"] = "ORD-1" }));

        result.Sent.Should().BeTrue();
        result.Code.Should().StartWith("PED-");
        _attempts.Items.Should().ContainSingle();
        var attempt = _attempts.Items[0];
        attempt.BusinessInboundContactIdSnapshot.Should().Be(contactId);
        attempt.ContactTypeSnapshot.Should().Be("domicilio");
        attempt.InboundAgentIdSnapshot.Should().Be(inboundAgentId);
        attempt.PickupAddressSnapshot.Should().Be("Calle 16 # 9-35, Centro, Valledupar");
        ReadPayload(attempt).Should().Contain("pickup_address", "Calle 16 # 9-35, Centro, Valledupar");
        sequenceContext!.Custom.Should().Contain("business_inbound_contact_id", contactId.ToString());
    }

    [Fact]
    public async Task EscalateAsync_WhenTargetAlreadyHasAttempt_DoesNotCallAnotherDeliveryContact()
    {
        var firstContactId = Guid.NewGuid();
        var secondContactId = Guid.NewGuid();
        _contacts.Items.Add(CreateContact(firstContactId, Guid.NewGuid(), "domicilio", "+57 300 000 0001", "domi_1"));
        _contacts.Items.Add(CreateContact(secondContactId, Guid.NewGuid(), "domicilio", "+57 300 000 0002", "domi_2"));
        _attempts.Items.Add(CreateAttempt(firstContactId, "+57 300 000 0001"));
        var service = CreateService(CreateConfig(firstContactId));

        var result = await service.EscalateAsync(new ExternalEscalationRequest(
            _sourceAgentId,
            "order_created",
            _targetId,
            new Dictionary<string, string>()));

        result.Sent.Should().BeFalse();
        result.Error.Should().Be("target_already_escalated");
        _attempts.Items.Should().HaveCount(1);
        _whatsApp.Verify(w => w.SendTextMessageAsync(It.IsAny<Guid>(), "573000000002", It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAttemptAsync_MarksAttemptAndPublishesConfiguredOutcome()
    {
        var contactId = Guid.NewGuid();
        var attempt = CreateAttempt(contactId, "+57 300 000 0001");
        _attempts.Items.Add(attempt);
        var service = CreateService(CreateConfig(contactId));

        var result = await service.CompleteAttemptAsync(new ExternalEscalationCompletionRequest(
            _businessId,
            attempt.ExternalEscalationAttemptId,
            "+57 300 000 0001",
            ExternalEscalationOutcomeKeys.Accepted,
            ExternalEscalationAttemptStatus.Accepted,
            "acepto",
            new Dictionary<string, string> { ["order_number"] = "ORD-1" }));

        result.Success.Should().BeTrue();
        attempt.Status.Should().Be(ExternalEscalationAttemptStatus.Accepted);
        attempt.OutcomeKey.Should().Be("accepted");
        attempt.CompletedAt.Should().NotBeNull();
        _outcomes.Verify(o => o.EnqueueAsync(
            _businessId,
            attempt.ExternalEscalationAttemptId,
            "accepted",
            It.Is<IReadOnlyDictionary<string, string>>(p =>
                p["outcome_key"] == "accepted" &&
                p["order_number"] == "ORD-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _outcomes.Verify(o => o.PublishAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredAttemptsAsync_MarksTimedOutAndReturnsExpiredAttemptsWithoutRetry()
    {
        var contactId = Guid.NewGuid();
        var secondContactId = Guid.NewGuid();
        _contacts.Items.Add(CreateContact(contactId, Guid.NewGuid(), "domicilio", "+57 300 000 0001", "domi_1"));
        _contacts.Items.Add(CreateContact(secondContactId, Guid.NewGuid(), "domicilio", "+57 300 000 0002", "domi_2"));
        var attempt = CreateAttempt(contactId, "+57 300 000 0001");
        attempt.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        _attempts.Items.Add(attempt);
        var service = CreateService(CreateConfig(contactId));

        var expired = await service.ProcessExpiredAttemptsAsync();

        expired.Should().ContainSingle();
        expired[0].AttemptId.Should().Be(attempt.ExternalEscalationAttemptId);
        expired[0].Payload.Should().Contain("outcome_key", "timed_out");
        attempt.Status.Should().Be(ExternalEscalationAttemptStatus.TimedOut);
        attempt.CompletedAt.Should().NotBeNull();
        attempt.OutcomeKey.Should().Be("timed_out");
        _attempts.Items.Should().ContainSingle();
        _whatsApp.Verify(w => w.SendTextMessageAsync(It.IsAny<Guid>(), "573000000002", It.IsAny<string>()), Times.Never);
    }

    private ExternalEscalationService CreateService(AgentConfig config)
    {
        var configProvider = new Mock<IAgentConfigProvider>();
        configProvider
            .Setup(p => p.GetConfigAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        return new ExternalEscalationService(
            CreateUnitOfWork().Object,
            () => configProvider.Object,
            _sequenceResolver.Object,
            _whatsApp.Object,
            () => _outcomes.Object);
    }

    private Mock<IUnitOfWork> CreateUnitOfWork()
    {
        var businessRepository = new Mock<IBusinessRepository>();
        businessRepository
            .Setup(r => r.GetByIdAsync(_businessId))
            .ReturnsAsync(new Business { BusinessId = _businessId, Name = "Vinos" });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Businesses).Returns(businessRepository.Object);
        unitOfWork.SetupGet(u => u.ExternalEscalationAttempts).Returns(_attempts);
        unitOfWork.SetupGet(u => u.BusinessInboundContacts).Returns(_contacts);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private AgentConfig CreateConfig(Guid configuredContactId) => new()
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
                        OutcomeEvents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [ExternalEscalationOutcomeKeys.Requested] = "delivery_requested",
                            [ExternalEscalationOutcomeKeys.Accepted] = "delivery_confirmed",
                            [ExternalEscalationOutcomeKeys.Declined] = "delivery_unavailable",
                            [ExternalEscalationOutcomeKeys.TimedOut] = "delivery_unavailable"
                        },
                        ContactType = "domicilio",
                        PickupAddress = "Calle 16 # 9-35, Centro, Valledupar",
                        AttemptTimeoutMinutes = 15,
                        AttemptCodePrefix = "PED",
                        SendMessageSequence = "domicilio_request",
                        Contacts =
                        [
                            new ExternalEscalationContactDefinition
                            {
                                BusinessInboundContactId = configuredContactId,
                                Priority = 1,
                            }
                        ]
                    }
                }
            }
        }
    };

    private BusinessInboundContact CreateContact(Guid contactId, Guid inboundAgentId, string type, string phone, string key = "domicilio") => new()
    {
        BusinessInboundContactId = contactId,
        BusinessId = _businessId,
        Type = type,
        Key = key,
        Name = key,
        Role = type,
        PhoneNumber = phone,
        PhoneNormalized = NormalizePhone(phone),
        InboundAgentId = inboundAgentId,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        InboundAgent = new Agent
        {
            AgentId = inboundAgentId,
            BusinessId = _businessId,
            Kind = type,
            Name = $"Agent {type}",
            IsActive = true
        }
    };

    private ExternalEscalationAttempt CreateAttempt(Guid contactId, string phone) => new()
    {
        ExternalEscalationAttemptId = Guid.NewGuid(),
        BusinessId = _businessId,
        SourceAgentId = _sourceAgentId,
        EventName = "order_created",
        TargetType = "order_created",
        TargetId = _targetId,
        ContactKey = "domicilio",
        ContactNameSnapshot = "Domicilio",
        ContactRoleSnapshot = "domicilio",
        ContactPhoneSnapshot = NormalizePhone(phone),
        InboundAgentIdSnapshot = Guid.NewGuid(),
        BusinessInboundContactIdSnapshot = contactId,
        ContactTypeSnapshot = "domicilio",
        AttemptCode = "PED-12345",
        CustomPayloadJson = "{}",
        Status = ExternalEscalationAttemptStatus.Pending,
        EscalatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddMinutes(15)
    };

    private static IReadOnlyDictionary<string, string> ReadPayload(ExternalEscalationAttempt attempt) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(attempt.CustomPayloadJson ?? "{}")!;

    private static string NormalizePhone(string phone) => new(phone.Where(char.IsDigit).ToArray());

    private sealed class FakeBusinessInboundContactRepository : IBusinessInboundContactRepository
    {
        public List<BusinessInboundContact> Items { get; } = [];

        public Task<BusinessInboundContact?> GetByIdAsync(Guid contactId, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(c => c.BusinessInboundContactId == contactId));

        public Task<BusinessInboundContact?> GetByPhoneAsync(Guid businessId, string normalizedPhone, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(c => c.BusinessId == businessId && c.PhoneNormalized == normalizedPhone));

        public Task<BusinessInboundContact?> GetActiveByPhoneAsync(Guid businessId, string phone, CancellationToken ct = default)
        {
            var normalized = NormalizePhone(phone);
            return Task.FromResult(Items.FirstOrDefault(c =>
                c.BusinessId == businessId &&
                c.PhoneNormalized == normalized &&
                c.IsActive &&
                c.InboundAgent.BusinessId == businessId &&
                c.InboundAgent.IsActive));
        }

        public Task<IReadOnlyList<BusinessInboundContact>> GetByBusinessAsync(Guid businessId, bool includeInactive = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BusinessInboundContact>>(Items.Where(c => c.BusinessId == businessId && (includeInactive || c.IsActive)).ToList());

        public Task<IReadOnlyList<BusinessInboundContact>> GetActiveByBusinessAsync(Guid businessId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BusinessInboundContact>>(Items.Where(c => c.BusinessId == businessId && c.IsActive).ToList());

        public Task<IReadOnlyList<BusinessInboundContact>> GetActiveByBusinessAndTypeAsync(Guid businessId, string type, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BusinessInboundContact>>(Items.Where(c =>
                c.BusinessId == businessId &&
                c.Type == type &&
                c.IsActive &&
                c.InboundAgent.BusinessId == businessId &&
                c.InboundAgent.IsActive).ToList());

        public Task<BusinessInboundContact> AddAsync(BusinessInboundContact contact, CancellationToken ct = default)
        {
            Items.Add(contact);
            return Task.FromResult(contact);
        }

        public Task UpdateAsync(BusinessInboundContact contact, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeExternalEscalationAttemptRepository : IExternalEscalationAttemptRepository
    {
        public List<ExternalEscalationAttempt> Items { get; } = [];

        public Task<ExternalEscalationAttempt?> GetByIdAsync(Guid attemptId, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(a => a.ExternalEscalationAttemptId == attemptId));

        public Task<ExternalEscalationAttempt?> GetByAttemptCodeAsync(Guid businessId, string attemptCode, string phone, CancellationToken ct = default)
        {
            var normalized = NormalizePhone(phone);
            return Task.FromResult(Items.FirstOrDefault(a =>
                a.BusinessId == businessId &&
                a.AttemptCode.Equals(attemptCode, StringComparison.OrdinalIgnoreCase) &&
                a.ContactPhoneSnapshot == normalized &&
                a.Status == ExternalEscalationAttemptStatus.Pending));
        }

        public Task<ExternalEscalationAttempt?> GetByWhatsAppMessageIdAsync(Guid businessId, string whatsAppMessageId, string phone, CancellationToken ct = default)
        {
            var normalized = NormalizePhone(phone);
            return Task.FromResult(Items.FirstOrDefault(a =>
                a.BusinessId == businessId &&
                a.WhatsAppMessageId == whatsAppMessageId &&
                a.ContactPhoneSnapshot == normalized &&
                a.Status == ExternalEscalationAttemptStatus.Pending));
        }

        public Task<ExternalEscalationAttempt?> GetLatestByAttemptCodeForContactAsync(Guid businessId, string attemptCode, string phone, CancellationToken ct = default)
        {
            var normalized = NormalizePhone(phone);
            return Task.FromResult(Items
                .Where(a => a.BusinessId == businessId && a.AttemptCode == attemptCode && a.ContactPhoneSnapshot == normalized)
                .OrderByDescending(a => a.EscalatedAt)
                .FirstOrDefault());
        }

        public Task<IReadOnlyList<ExternalEscalationAttempt>> GetRecentByContactPhoneAsync(Guid businessId, string phone, int limit, bool includeCompleted = false, CancellationToken ct = default)
        {
            var normalized = NormalizePhone(phone);
            return Task.FromResult<IReadOnlyList<ExternalEscalationAttempt>>(Items
                .Where(a => a.BusinessId == businessId && a.ContactPhoneSnapshot == normalized)
                .Where(a => includeCompleted || a.Status == ExternalEscalationAttemptStatus.Pending)
                .OrderByDescending(a => a.EscalatedAt)
                .Take(Math.Clamp(limit, 1, 20))
                .ToList());
        }

        public Task<IReadOnlyList<ExternalEscalationAttempt>> GetPendingByContactPhoneAsync(Guid businessId, string phone, CancellationToken ct = default)
        {
            var normalized = NormalizePhone(phone);
            return Task.FromResult<IReadOnlyList<ExternalEscalationAttempt>>(Items.Where(a =>
                a.BusinessId == businessId &&
                a.ContactPhoneSnapshot == normalized &&
                a.Status == ExternalEscalationAttemptStatus.Pending).ToList());
        }

        public Task<IReadOnlyList<ExternalEscalationAttempt>> GetExpiredPendingAttemptsAsync(DateTime utcNow, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExternalEscalationAttempt>>(Items.Where(a => a.Status == ExternalEscalationAttemptStatus.Pending && a.ExpiresAt <= utcNow).ToList());

        public Task<int> CountAttemptsAsync(Guid businessId, string eventName, string targetType, Guid targetId, CancellationToken ct = default) =>
            Task.FromResult(Items.Count(a => a.BusinessId == businessId && a.EventName == eventName && a.TargetType == targetType && a.TargetId == targetId));

        public Task<IReadOnlyList<ExternalEscalationAttempt>> GetAttemptsForTargetAsync(Guid businessId, string eventName, string targetType, Guid targetId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExternalEscalationAttempt>>(Items.Where(a => a.BusinessId == businessId && a.EventName == eventName && a.TargetType == targetType && a.TargetId == targetId).ToList());

        public Task<ExternalEscalationAttempt> AddAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default)
        {
            Items.Add(attempt);
            return Task.FromResult(attempt);
        }

        public Task<ExternalEscalationAttempt> UpdateAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default) =>
            Task.FromResult(attempt);

    }
}
