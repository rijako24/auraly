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
    private readonly Mock<IConversationRepository> _mockConversationRepository;
    private readonly Mock<ILogger<MessageService>> _mockLogger;
    private readonly MessageService _service;

    public MessageServiceTests()
    {
        _mockUnitOfWork             = new Mock<IUnitOfWork>();
        _mockMessageRepository      = new Mock<IMessageRepository>();
        _mockConversationRepository = new Mock<IConversationRepository>();
        _mockLogger                 = new Mock<ILogger<MessageService>>();

        _mockUnitOfWork.Setup(u => u.Messages).Returns(_mockMessageRepository.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepository.Object);

        _service = new MessageService(_mockUnitOfWork.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SaveMessageAsync_User_ShouldSaveMessageAndUpdateConversationLastMessage()
    {
        var conversationId = Guid.NewGuid();
        var sender         = "User";
        var messageText    = "Hola, quiero información";

        var conversation = new Conversation
        {
            ConversationId = conversationId,
            BusinessId     = Guid.NewGuid(),
            UserNumber     = "3001234567"
        };

        _mockMessageRepository
            .Setup(x => x.CreateAsync(It.IsAny<Message>()))
            .ReturnsAsync((Message m) => m);

        _mockConversationRepository
            .Setup(x => x.GetByIdAsync(conversationId))
            .ReturnsAsync(conversation);

        _mockConversationRepository
            .Setup(x => x.UpdateAsync(It.IsAny<Conversation>()))
            .ReturnsAsync((Conversation c) => c);

        _mockUnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _service.SaveMessageAsync(conversationId, sender, messageText);

        result.Should().NotBeNull();
        result.ConversationId.Should().Be(conversationId);
        result.Sender.Should().Be(sender);
        result.MessageText.Should().Be(messageText);

        _mockMessageRepository.Verify(x => x.CreateAsync(It.Is<Message>(m =>
            m.ConversationId == conversationId &&
            m.Sender         == sender         &&
            m.MessageText    == messageText)), Times.Once);

        _mockConversationRepository.Verify(x => x.GetByIdAsync(conversationId), Times.Once);
        _mockConversationRepository.Verify(x => x.UpdateAsync(It.Is<Conversation>(c =>
            c.LastMessage == messageText)), Times.Once);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveMessageAsync_Bot_ShouldNotTouchConversation()
    {
        var conversationId = Guid.NewGuid();
        var messageText    = "Respuesta del bot";

        _mockMessageRepository
            .Setup(x => x.CreateAsync(It.IsAny<Message>()))
            .ReturnsAsync((Message m) => m);

        _mockUnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.SaveMessageAsync(conversationId, "Bot", messageText);

        _mockConversationRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        _mockConversationRepository.Verify(x => x.UpdateAsync(It.IsAny<Conversation>()), Times.Never);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetConversationHistoryAsync_ShouldReturnMessages()
    {
        var conversationId   = Guid.NewGuid();
        var expectedMessages = new List<Message>
        {
            new() { MessageId = Guid.NewGuid(), ConversationId = conversationId, Sender = "User", MessageText = "Hola" },
            new() { MessageId = Guid.NewGuid(), ConversationId = conversationId, Sender = "Bot",  MessageText = "Hola, ¿en qué puedo ayudarte?" }
        };

        _mockMessageRepository
            .Setup(x => x.GetByConversationIdAsync(conversationId))
            .ReturnsAsync(expectedMessages);

        var result = await _service.GetConversationHistoryAsync(conversationId);

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        _mockMessageRepository.Verify(x => x.GetByConversationIdAsync(conversationId), Times.Once);
    }
}
