using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public class MessageServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IMessageRepository> _mockMessageRepository;
    private readonly Mock<IAIService> _mockAIService;
    private readonly Mock<ILogger<MessageService>> _mockLogger;
    private readonly MessageService _service;

    public MessageServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockMessageRepository = new Mock<IMessageRepository>();
        _mockAIService = new Mock<IAIService>();
        _mockLogger = new Mock<ILogger<MessageService>>();

        _mockUnitOfWork.Setup(u => u.Messages).Returns(_mockMessageRepository.Object);

        _service = new MessageService(_mockUnitOfWork.Object, _mockAIService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SaveMessageAsync_WithValidData_ShouldSaveAndReturnMessage()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var sender = "User";
        var messageText = "Hola, quiero información";
        var intent = "Greeting";

        var expectedMessage = new Message
        {
            MessageId = Guid.NewGuid(),
            ConversationId = conversationId,
            Sender = sender,
            MessageText = messageText,
            Intent = intent,
            Timestamp = DateTime.UtcNow
        };

        _mockMessageRepository
            .Setup(x => x.CreateAsync(It.IsAny<Message>()))
            .ReturnsAsync(expectedMessage);

        _mockUnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.SaveMessageAsync(conversationId, sender, messageText, intent);

        // Assert
        result.Should().NotBeNull();
        result.ConversationId.Should().Be(conversationId);
        result.Sender.Should().Be(sender);
        result.MessageText.Should().Be(messageText);
        result.Intent.Should().Be(intent);

        _mockMessageRepository.Verify(x => x.CreateAsync(It.Is<Message>(m =>
            m.ConversationId == conversationId &&
            m.Sender == sender &&
            m.MessageText == messageText &&
            m.Intent == intent)), Times.Once);

        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClassifyIntentAsync_ShouldCallAIService()
    {
        // Arrange
        var messageText = "¿Cuánto cuesta?";
        var conversation = new Conversation { ConversationId = Guid.NewGuid() };
        var expectedIntent = "AskPrice";

        _mockAIService
            .Setup(x => x.ClassifyIntentAsync(messageText, conversation))
            .ReturnsAsync(expectedIntent);

        // Act
        var result = await _service.ClassifyIntentAsync(messageText, conversation);

        // Assert
        result.Should().Be(expectedIntent);
        _mockAIService.Verify(x => x.ClassifyIntentAsync(messageText, conversation), Times.Once);
    }

    [Fact]
    public async Task GetConversationHistoryAsync_ShouldReturnMessages()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var expectedMessages = new List<Message>
        {
            new Message { MessageId = Guid.NewGuid(), ConversationId = conversationId, Sender = "User", MessageText = "Hola" },
            new Message { MessageId = Guid.NewGuid(), ConversationId = conversationId, Sender = "Bot", MessageText = "Hola, ¿en qué puedo ayudarte?" }
        };

        _mockMessageRepository
            .Setup(x => x.GetByConversationIdAsync(conversationId))
            .ReturnsAsync(expectedMessages);

        // Act
        var result = await _service.GetConversationHistoryAsync(conversationId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        _mockMessageRepository.Verify(x => x.GetByConversationIdAsync(conversationId), Times.Once);
    }
}
