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
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConversationRepository> _mockConversationRepository;
    private readonly Mock<ILogger<ConversationService>> _mockLogger;
    private readonly ConversationService _service;

    public ConversationServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockConversationRepository = new Mock<IConversationRepository>();
        _mockLogger = new Mock<ILogger<ConversationService>>();

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepository.Object);

        _service = new ConversationService(_mockUnitOfWork.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetOrCreateConversationAsync_WhenConversationExists_ShouldReturnExistingConversation()
    {
        // Arrange
        var businessId = Guid.NewGuid();
        var userNumber = "1234567890";
        var existingConversation = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            Timestamp = DateTime.UtcNow
        };

        _mockConversationRepository
            .Setup(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber))
            .ReturnsAsync(existingConversation);

        // Act
        var result = await _service.GetOrCreateConversationAsync(businessId, userNumber);

        // Assert
        result.Should().NotBeNull();
        result.ConversationId.Should().Be(existingConversation.ConversationId);
        result.UserNumber.Should().Be(userNumber);
        
        _mockConversationRepository.Verify(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber), Times.Once);
        _mockConversationRepository.Verify(x => x.CreateAsync(It.IsAny<Conversation>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateConversationAsync_WhenConversationDoesNotExist_ShouldCreateNewConversation()
    {
        // Arrange
        var businessId = Guid.NewGuid();
        var userNumber = "1234567890";
        var customerName = "Juan Pérez";

        _mockConversationRepository
            .Setup(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber))
            .ReturnsAsync((Conversation?)null);

        var newConversation = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            CustomerName = customerName,
            Timestamp = DateTime.UtcNow
        };

        _mockConversationRepository
            .Setup(x => x.CreateAsync(It.IsAny<Conversation>()))
            .ReturnsAsync(newConversation);

        _mockUnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.GetOrCreateConversationAsync(businessId, userNumber, customerName);

        // Assert
        result.Should().NotBeNull();
        result.UserNumber.Should().Be(userNumber);
        result.CustomerName.Should().Be(customerName);

        _mockConversationRepository.Verify(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber), Times.Once);
        _mockConversationRepository.Verify(x => x.CreateAsync(It.Is<Conversation>(c =>
            c.BusinessId == businessId &&
            c.UserNumber == userNumber &&
            c.CustomerName == customerName)), Times.Once);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateConversationAsync_WhenExistingWithoutAgentId_ShouldPersistAgentId()
    {
        var businessId = Guid.NewGuid();
        var userNumber = "3001234567";
        var agentId = Guid.NewGuid();
        var existingConversation = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            AgentId = null,
            Timestamp = DateTime.UtcNow
        };

        _mockConversationRepository
            .Setup(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber))
            .ReturnsAsync(existingConversation);

        _mockConversationRepository
            .Setup(x => x.UpdateAsync(It.IsAny<Conversation>()))
            .ReturnsAsync(existingConversation);

        _mockUnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _service.GetOrCreateConversationAsync(businessId, userNumber, agentId: agentId);

        result.AgentId.Should().Be(agentId);
        _mockConversationRepository.Verify(x => x.UpdateAsync(It.Is<Conversation>(c => c.AgentId == agentId)), Times.Once);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
