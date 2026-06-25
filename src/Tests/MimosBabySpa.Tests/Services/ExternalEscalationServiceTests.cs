using System.Text.Json;
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

public sealed class ExternalEscalationServiceTests
{
    private readonly Guid _businessId = Guid.NewGuid();
    private readonly Guid _sourceAgentId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly FakeExternalEscalationAttemptRepository _attempts = new();
    private readonly FakeBusinessInboundContactRepository _contacts = new();
    private readonly Mock<IMessageSequenceResolver> _sequenceResolver = new();
    private readonly Mock<IWhatsAppService> _whatsApp = new();
    private readonly Mock<IEventNotificationDispatcher> _notifications = new();
    private readonly RecordingExternalEscalationTargetHandler _targetHandler = new();

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
    public async Task EscalateNextAsync_UsesConfiguredInboundContactAndPickupAddressFromEvent()
    {
        var contactId = Guid.NewGuid();
        var inboundAgentId = Guid.NewGuid();
        _contacts.Items.Add(CreateContact(contactId, inboundAgentId, "delivery", "+57 304 205 2007"));
        var service = CreateService(CreateConfig(contactId, retryEnabled: true));
        MessageSequenceContext? sequenceContext = null;
        _sequenceResolver
            .Setup(r => r.ResolveAsync(
                _businessId,
                "delivery_request",
                It.IsAny<MessageSequenceCatalog>(),
                It.IsAny<MessageSequenceContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MessageSequenceCatalog, MessageSequenceContext, CancellationToken>((_, _, _, ctx, _) => sequenceContext = ctx)
            .ReturnsAsync([new OutboundMessage("Solicitud de domicilio", null)]);

        var result = await service.EscalateNextAsync(new ExternalEscalationRequest(
            _sourceAgentId,
            "order_created",
            "order",
            _targetId,
            new Dictionary<string, string> { ["order_id"] = "ORD-1" }));

        result.Sent.Should().BeTrue();
        result.Code.Should().StartWith("PED-");
        _attempts.Items.Should().ContainSingle();
        var attempt = _attempts.Items[0];
        attempt.BusinessInboundContactIdSnapshot.Should().Be(contactId);
        attempt.ContactTypeSnapshot.Should().Be("delivery");
        attempt.InboundAgentIdSnapshot.Should().Be(inboundAgentId);
        attempt.PickupAddressSnapshot.Should().Be("Calle 16 # 9-35, Centro, Valledupar");
        ReadPayload(attempt).Should().Contain("pickup_address", "Calle 16 # 9-35, Centro, Valledupar");
        sequenceContext!.Custom.Should().Contain("business_inbound_contact_id", contactId.ToString());
        _notifications.Verify(n => n.SendEventAsync(
            _businessId,
            It.IsAny<AgentConfig>(),
            "delivery_requested",
            It.Is<IReadOnlyDictionary<string, string>>(p =>
                p["external_interaction_id"] == attempt.ExternalEscalationAttemptId.ToString() &&
                p["pickup_address"] == "Calle 16 # 9-35, Centro, Valledupar"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EscalateNextAsync_WhenRetryEnabled_RetriesNextActiveContactOfSameType()
    {
        var firstContactId = Guid.NewGuid();
        var secondContactId = Guid.NewGuid();
        _contacts.Items.Add(CreateContact(firstContactId, Guid.NewGuid(), "delivery", "+57 300 000 0001", "domi_1"));
        _contacts.Items.Add(CreateContact(secondContactId, Guid.NewGuid(), "delivery", "+57 300 000 0002", "domi_2"));
        _attempts.Items.Add(CreateAttempt(firstContactId, "+57 300 000 0001"));
        var service = CreateService(CreateConfig(firstContactId, retryEnabled: true));

        var result = await service.EscalateNextAsync(new ExternalEscalationRequest(
            _sourceAgentId,
            "order_created",
            "order",
            _targetId,
            new Dictionary<string, string>()));

        result.Sent.Should().BeTrue();
        _attempts.Items.Last().BusinessInboundContactIdSnapshot.Should().Be(secondContactId);
        _attempts.Items.Last().ContactPhoneSnapshot.Should().Be("573000000002");
    }

    [Fact]
    public async Task EscalateNextAsync_WhenRetryDisabled_DoesNotRetryOtherContactsOfSameType()
    {
        var firstContactId = Guid.NewGuid();
        var secondContactId = Guid.NewGuid();
        _contacts.Items.Add(CreateContact(firstContactId, Guid.NewGuid(), "delivery", "+57 300 000 0001", "domi_1"));
        _contacts.Items.Add(CreateContact(secondContactId, Guid.NewGuid(), "delivery", "+57 300 000 0002", "domi_2"));
        _attempts.Items.Add(CreateAttempt(firstContactId, "+57 300 000 0001"));
        var service = CreateService(CreateConfig(firstContactId, retryEnabled: false));

        var result = await service.EscalateNextAsync(new ExternalEscalationRequest(
            _sourceAgentId,
            "order_created",
            "order",
            _targetId,
            new Dictionary<string, string>()));

        result.Sent.Should().BeFalse();
        result.Error.Should().Be("no_more_contacts");
        _attempts.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ResolveAttemptAsync_UsesInteractivePayloadAndReturnsRequestedAction()
    {
        var attempt = CreateAttempt(Guid.NewGuid(), "+57 300 000 0001");
        _attempts.Items.Add(attempt);
        var service = CreateService(CreateConfig(attempt.BusinessInboundContactIdSnapshot!.Value, retryEnabled: true));

        var result = await service.ResolveAttemptAsync(
            _businessId,
            "+57 300 000 0001",
            "confirmo",
            $"external_interaction:accepted:{attempt.ExternalEscalationAttemptId:N}",
            null);

        result.Resolution.Should().Be("resolved");
        result.Attempt.Should().BeSameAs(attempt);
        result.RequestedAction.Should().Be("accepted");
    }

    [Fact]
    public async Task CompleteAsync_AcceptsAttempt_CancelsOtherPendingAttemptsAndSendsAcceptedNotification()
    {
        var contactId = Guid.NewGuid();
        var attempt = CreateAttempt(contactId, "+57 300 000 0001");
        var competingAttempt = CreateAttempt(Guid.NewGuid(), "+57 300 000 0002");
        _attempts.Items.Add(attempt);
        _attempts.Items.Add(competingAttempt);
        var service = CreateService(CreateConfig(contactId, retryEnabled: true));

        var result = await service.CompleteAsync(
            _businessId,
            attempt.ExternalEscalationAttemptId,
            "+57 300 000 0001",
            "accepted",
            "Lo tomo",
            new Dictionary<string, string> { ["driver"] = "Luis" });

        result.Success.Should().BeTrue();
        result.OutcomeKey.Should().Be("accepted");
        attempt.Status.Should().Be(ExternalEscalationAttemptStatus.Accepted);
        attempt.CompletedAt.Should().NotBeNull();
        competingAttempt.Status.Should().Be(ExternalEscalationAttemptStatus.Cancelled);
        _targetHandler.Completed.Should().ContainSingle(c => c.AttemptId == attempt.ExternalEscalationAttemptId && c.OutcomeKey == "accepted");
        _notifications.Verify(n => n.SendEventAsync(
            _businessId,
            It.IsAny<AgentConfig>(),
            "delivery_confirmed",
            It.Is<IReadOnlyDictionary<string, string>>(p => p["outcome_key"] == "accepted" && p["driver"] == "Luis"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private ExternalEscalationService CreateService(AgentConfig config)
    {
        var configProvider = new Mock<IAgentConfigProvider>();
        configProvider
            .Setup(p => p.GetConfigAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        return new ExternalEscalationService(
            CreateUnitOfWork().Object,
            configProvider.Object,
            _sequenceResolver.Object,
            _whatsApp.Object,
            _notifications.Object,
            [_targetHandler],
            NullLogger<ExternalEscalationService>.Instance);
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

    private AgentConfig CreateConfig(Guid configuredContactId, bool retryEnabled) => new()
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
                        ContactType = "delivery",
                        PickupAddress = "Calle 16 # 9-35, Centro, Valledupar",
                        AttemptTimeoutMinutes = 15,
                        AttemptCodePrefix = "PED",
                        SendMessageSequence = "delivery_request",
                        AttemptSentNotificationEvent = "delivery_requested",
                        AcceptedNotificationEvent = "delivery_confirmed",
                        ExhaustedNotificationEvent = "delivery_unavailable",
                        Contacts =
                        [
                            new ExternalEscalationContactDefinition
                            {
                                BusinessInboundContactId = configuredContactId,
                                Priority = 1,
                                RetryEnabled = retryEnabled
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
        TargetType = "order",
        TargetId = _targetId,
        ContactKey = "domicilio",
        ContactNameSnapshot = "Domicilio",
        ContactRoleSnapshot = "delivery",
        ContactPhoneSnapshot = NormalizePhone(phone),
        InboundAgentIdSnapshot = Guid.NewGuid(),
        BusinessInboundContactIdSnapshot = contactId,
        ContactTypeSnapshot = "delivery",
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

        public Task<bool> HasAcceptedForTargetAsync(Guid businessId, string eventName, string targetType, Guid targetId, CancellationToken ct = default) =>
            Task.FromResult(Items.Any(a => a.BusinessId == businessId && a.EventName == eventName && a.TargetType == targetType && a.TargetId == targetId && a.Status == ExternalEscalationAttemptStatus.Accepted));

        public Task<ExternalEscalationAttempt> AddAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default)
        {
            Items.Add(attempt);
            return Task.FromResult(attempt);
        }

        public Task<ExternalEscalationAttempt> UpdateAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default) =>
            Task.FromResult(attempt);

        public Task CancelPendingForTargetAsync(Guid businessId, string eventName, string targetType, Guid targetId, Guid exceptAttemptId, CancellationToken ct = default)
        {
            foreach (var attempt in Items.Where(a =>
                a.BusinessId == businessId &&
                a.EventName == eventName &&
                a.TargetType == targetType &&
                a.TargetId == targetId &&
                a.ExternalEscalationAttemptId != exceptAttemptId &&
                a.Status == ExternalEscalationAttemptStatus.Pending))
            {
                attempt.Status = ExternalEscalationAttemptStatus.Cancelled;
                attempt.CancelledAt = DateTime.UtcNow;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingExternalEscalationTargetHandler : IExternalEscalationTargetHandler
    {
        public List<(Guid AttemptId, string OutcomeKey)> Completed { get; } = [];

        public bool CanHandle(string targetType, string eventName) => true;

        public Task OnAttemptSentAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) => Task.CompletedTask;

        public Task OnAttemptCompletedAsync(ExternalEscalationAttempt attempt, ExternalEscalationCompletion completion, CancellationToken ct = default)
        {
            Completed.Add((attempt.ExternalEscalationAttemptId, completion.OutcomeKey));
            return Task.CompletedTask;
        }

        public Task OnAttemptDeclinedAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) => Task.CompletedTask;

        public Task OnAttemptTimedOutAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) => Task.CompletedTask;

        public Task OnAttemptsExhaustedAsync(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default) => Task.CompletedTask;
    }
}