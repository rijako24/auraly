using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
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
