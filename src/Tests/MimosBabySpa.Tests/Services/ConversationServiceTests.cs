using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public class ConversationServiceTests
{
    private readonly Mock<IConversationLifecycleService> _mockLifecycle;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConversationRepository> _mockConversationRepository;
    private readonly Mock<ILogger<ConversationService>> _mockLogger;
    private readonly ConversationService _service;

    public ConversationServiceTests()
    {
        _mockLifecycle = new Mock<IConversationLifecycleService>();
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockConversationRepository = new Mock<IConversationRepository>();
        _mockLogger = new Mock<ILogger<ConversationService>>();

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepository.Object);

        _service = new ConversationService(
            _mockLifecycle.Object,
            _mockUnitOfWork.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetOrCreateConversationAsync_DelegatesToLifecycleService()
    {
        var businessId = Guid.NewGuid();
        var userNumber = "1234567890";
        var expected = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber
        };

        _mockLifecycle
            .Setup(x => x.GetOrOpenForCustomerAsync(businessId, userNumber, null, default))
            .ReturnsAsync(expected);

        var result = await _service.GetOrCreateConversationAsync(businessId, userNumber);

        result.Should().BeSameAs(expected);
        _mockLifecycle.Verify(
            x => x.GetOrOpenForCustomerAsync(businessId, userNumber, null, default),
            Times.Once);
    }

    [Fact]
    public async Task UpdateConversationContextAsync_DelegatesToLifecycleTouch()
    {
        var conversationId = Guid.NewGuid();

        await _service.UpdateConversationContextAsync(conversationId, "Hola");

        _mockLifecycle.Verify(
            x => x.TouchActivityAsync(conversationId, "Hola", default),
            Times.Once);
    }

    [Fact]
    public async Task GetConversationByIdAsync_DelegatesToRepository()
    {
        var conversationId = Guid.NewGuid();
        var expected = new Conversation { ConversationId = conversationId };

        _mockConversationRepository
            .Setup(x => x.GetByIdAsync(conversationId))
            .ReturnsAsync(expected);

        var result = await _service.GetConversationByIdAsync(conversationId);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task HasClosedConversationsAsync_DelegatesToRepository()
    {
        var businessId = Guid.NewGuid();
        var userNumber = "1234567890";

        _mockConversationRepository
            .Setup(x => x.HasClosedConversationsAsync(businessId, userNumber, default))
            .ReturnsAsync(true);

        var result = await _service.HasClosedConversationsAsync(businessId, userNumber);

        result.Should().BeTrue();
    }
}
