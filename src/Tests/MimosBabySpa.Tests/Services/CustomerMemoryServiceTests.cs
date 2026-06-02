using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public class CustomerMemoryServiceTests
{
    [Fact]
    public async Task RememberAsync_DelegatesUpsertToRepository()
    {
        var businessId = Guid.NewGuid();
        var mockRepo = new Mock<ICustomerMemoryRepository>();
        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.CustomerMemory).Returns(mockRepo.Object);
        mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var service = new CustomerMemoryService(mockUow.Object);

        await service.RememberAsync(businessId, "+573001234567", "customer_name", "Ana");

        mockRepo.Verify(r => r.UpsertAsync(
            businessId, "+573001234567", "customer_name", "Ana", It.IsAny<CancellationToken>()), Times.Once);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_MapsRepositoryRowsToDictionary()
    {
        var businessId = Guid.NewGuid();
        var mockRepo = new Mock<ICustomerMemoryRepository>();
        mockRepo
            .Setup(r => r.GetByBusinessAndUserNumberAsync(businessId, "+573001234567", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CustomerMemory>
            {
                new()
                {
                    BusinessId = businessId,
                    UserNumber = "+573001234567",
                    Field = "customer_name",
                    Value = "Ana"
                }
            });

        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.CustomerMemory).Returns(mockRepo.Object);

        var service = new CustomerMemoryService(mockUow.Object);
        var all = await service.GetAllAsync(businessId, "+573001234567");

        all["customer_name"].Should().Be("Ana");
    }
}

public class ConversationSummaryHookTests
{
    [Fact]
    public async Task OnClosedAsync_ReservationConfirmed_AppendsSummaryLine()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = businessId,
            ConversationId = conversationId,
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc),
            Service = new Service { ServiceName = "Plan Marineritos" }
        };

        var mockReservations = new Mock<IReservationRepository>();
        mockReservations
            .Setup(r => r.GetActiveByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var mockContexts = new Mock<IConversationContextRepository>();
        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.Reservations).Returns(mockReservations.Object);
        mockUow.Setup(u => u.ConversationContexts).Returns(mockContexts.Object);

        var storedSummary = string.Empty;
        var mockMemory = new Mock<ICustomerMemoryService>();
        mockMemory
            .Setup(m => m.GetAsync(
                businessId, "+573001234567", CustomerMemoryKeys.Summary, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => string.IsNullOrWhiteSpace(storedSummary) ? null : storedSummary);
        mockMemory
            .Setup(m => m.RememberAsync(
                businessId,
                "+573001234567",
                CustomerMemoryKeys.Summary,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string, CancellationToken>((_, _, _, value, _) => storedSummary = value)
            .Returns(Task.CompletedTask);

        var hook = new ConversationSummaryHook(
            mockUow.Object,
            mockMemory.Object,
            NullLogger<ConversationSummaryHook>.Instance);

        var conversation = new Conversation
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            UserNumber = "+573001234567",
            ClosedAt = new DateTime(2026, 6, 1, 18, 0, 0, DateTimeKind.Utc)
        };

        await hook.OnClosedAsync(conversation, ConversationCloseReasons.ReservationConfirmed);

        storedSummary.Should().Contain("reservó Plan Marineritos");
        storedSummary.Should().Contain("2026-06-05");
    }

    [Fact]
    public async Task OnClosedAsync_DayChangedWithoutReservation_AppendsIntentLine()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        var mockReservations = new Mock<IReservationRepository>();
        mockReservations
            .Setup(r => r.GetActiveByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reservation?)null);

        var mockContexts = new Mock<IConversationContextRepository>();
        mockContexts
            .Setup(c => c.GetByConversationIdAsync(conversationId))
            .ReturnsAsync(new List<ConversationContext>
            {
                new() { Field = ConversationFactKeys.Service, Value = "Plan Marineritos" },
                new() { Field = ConversationFactKeys.DesiredDate, Value = "2026-06-05" }
            });

        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.Reservations).Returns(mockReservations.Object);
        mockUow.Setup(u => u.ConversationContexts).Returns(mockContexts.Object);

        var storedSummary = string.Empty;
        var mockMemory = new Mock<ICustomerMemoryService>();
        mockMemory
            .Setup(m => m.GetAsync(
                businessId, "+573001234567", CustomerMemoryKeys.Summary, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => string.IsNullOrWhiteSpace(storedSummary) ? null : storedSummary);
        mockMemory
            .Setup(m => m.RememberAsync(
                businessId,
                "+573001234567",
                CustomerMemoryKeys.Summary,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string, CancellationToken>((_, _, _, value, _) => storedSummary = value)
            .Returns(Task.CompletedTask);

        var hook = new ConversationSummaryHook(
            mockUow.Object,
            mockMemory.Object,
            NullLogger<ConversationSummaryHook>.Instance);

        var conversation = new Conversation
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            UserNumber = "+573001234567",
            ClosedAt = new DateTime(2026, 6, 1, 18, 0, 0, DateTimeKind.Utc)
        };

        await hook.OnClosedAsync(conversation, ConversationCloseReasons.DayChanged);

        storedSummary.Should().Contain("consultó");
        storedSummary.Should().Contain("no reservó");
    }
}

public class MessageRepositoryRecentTests
{
    [Fact]
    public async Task GetRecentByConversationIdAsync_ReturnsLastMessagesInAscendingOrder()
    {
        var conversationId = Guid.NewGuid();
        var repo = new RecentMessageRepositoryFake();

        for (var i = 0; i < 5; i++)
        {
            await repo.CreateAsync(new Message
            {
                MessageId = Guid.NewGuid(),
                ConversationId = conversationId,
                Sender = "user",
                MessageText = $"msg-{i}",
                Timestamp = new DateTime(2026, 6, 1, 10, i, 0, DateTimeKind.Utc)
            });
        }

        var recent = await repo.GetRecentByConversationIdAsync(conversationId, 3);
        recent.Should().HaveCount(3);
        recent[0].MessageText.Should().Be("msg-2");
        recent[2].MessageText.Should().Be("msg-4");
    }

    private sealed class RecentMessageRepositoryFake : IMessageRepository
    {
        private readonly List<Message> _store = [];

        public Task<Message> CreateAsync(Message message)
        {
            _store.Add(message);
            return Task.FromResult(message);
        }

        public Task<IEnumerable<Message>> GetByConversationIdAsync(Guid conversationId) =>
            Task.FromResult<IEnumerable<Message>>(_store.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Timestamp));

        public Task<IReadOnlyList<Message>> GetRecentByConversationIdAsync(
            Guid conversationId, int limit, CancellationToken ct = default)
        {
            if (limit <= 0)
                return Task.FromResult<IReadOnlyList<Message>>([]);

            var recent = _store
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToList();

            recent.Reverse();
            return Task.FromResult<IReadOnlyList<Message>>(recent);
        }

        public Task<Message?> GetByIdAsync(Guid messageId) =>
            Task.FromResult(_store.FirstOrDefault(m => m.MessageId == messageId));

        public Task<(IReadOnlyList<Message> Items, int TotalCount)> GetPagedByConversationIdAsync(
            Guid conversationId, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
