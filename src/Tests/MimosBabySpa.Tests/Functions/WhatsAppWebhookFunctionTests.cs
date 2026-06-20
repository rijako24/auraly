using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MimosBabySpa.API.Functions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using DomainMessage = MimosBabySpa.Domain.Entities.Message;
using DtoMessage = MimosBabySpa.Application.DTOs.Message;
using MimosBabySpa.Tests.Helpers;
using Xunit;

namespace MimosBabySpa.Tests.Functions;

public class WhatsAppWebhookFunctionTests
{
    private readonly Mock<IWhatsAppMessageProcessorService> _mockMessageProcessorService;
    private readonly Mock<IWhatsAppWebhookParserService> _mockWebhookParserService;
    private readonly Mock<IWhatsAppService> _mockWhatsAppService;
    private readonly Mock<IBusinessIdentificationService> _mockBusinessIdentificationService;
    private readonly Mock<IInboundMessageDeduplicationService> _mockDeduplicationService;
    private readonly Mock<ILogger<WhatsAppWebhookFunction>> _mockLogger;
    private readonly WhatsAppWebhookFunction _function;

    public WhatsAppWebhookFunctionTests()
    {
        _mockMessageProcessorService = new Mock<IWhatsAppMessageProcessorService>();
        _mockWebhookParserService = new Mock<IWhatsAppWebhookParserService>();
        _mockWhatsAppService = new Mock<IWhatsAppService>();
        _mockBusinessIdentificationService = new Mock<IBusinessIdentificationService>();
        _mockDeduplicationService = new Mock<IInboundMessageDeduplicationService>();
        _mockLogger = new Mock<ILogger<WhatsAppWebhookFunction>>();

        _mockDeduplicationService
            .Setup(x => x.TryBeginProcessingAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _function = new WhatsAppWebhookFunction(
            _mockMessageProcessorService.Object,
            _mockWebhookParserService.Object,
            _mockWhatsAppService.Object,
            _mockBusinessIdentificationService.Object,
            _mockDeduplicationService.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task Run_GetRequest_WithValidWebhookVerification_ShouldReturnChallenge()
    {
        // Arrange
        var challenge = "test_challenge_123";
        var request = CreateMockHttpRequestData("GET", $"?hub.mode=subscribe&hub.verify_token=test_token&hub.challenge={challenge}");
        
        _mockMessageProcessorService
            .Setup(x => x.VerifyWebhookAsync("subscribe", "test_token", challenge))
            .ReturnsAsync(challenge);

        // Act
        var response = await _function.Run(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockMessageProcessorService.Verify(x => x.VerifyWebhookAsync("subscribe", "test_token", challenge), Times.Once);
    }

    [Fact]
    public async Task Run_GetRequest_WithInvalidWebhookVerification_ShouldReturnForbidden()
    {
        // Arrange
        var request = CreateMockHttpRequestData("GET", "?hub.mode=subscribe&hub.verify_token=wrong_token&hub.challenge=test");
        
        _mockMessageProcessorService
            .Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        var response = await _function.Run(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Run_PostRequest_WithValidMessage_ShouldProcessMessage()
    {
        // Arrange
        var userNumber = "1234567890";
        var messageText = "Hola, quiero información";
        var customerName = "Juan Pérez";
        var businessId = Guid.NewGuid();
        
        var webhookDto = new WhatsAppWebhookDto
        {
            Entry = new List<Entry>
            {
                new Entry
                {
                    Id = "entry_id",
                    Changes = new List<Change>
                    {
                        new Change
                        {
                            Field = "messages",
                            Value = new Value
                            {
                                Metadata = new WebhookMetadata { PhoneNumberId = "entry_id" },
                                Messages = new List<DtoMessage>
                                {
                                    new DtoMessage
                                    {
                                        Id = "wamid.test123",
                                        From = userNumber,
                                        Type = "text",
                                        Text = new TextMessage { Body = messageText }
                                    }
                                },
                                Contacts = new List<Contact>
                                {
                                    new Contact
                                    {
                                        Profile = new Profile { Name = customerName }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var businessContext = new BusinessContext
        {
            BusinessId = businessId,
            BusinessName = "Test Business",
            WhatsAppNumber = new BusinessWhatsAppNumberDto
            {
                WhatsAppPhoneNumberId = "entry_id",
                WhatsAppAccessToken = "test_token"
            }
        };

        var incomingMessage = new IncomingMessage
        {
            UserNumber = userNumber,
            MessageText = messageText,
            CustomerName = customerName
        };

        var request = CreateMockHttpRequestData("POST", "", JsonSerializer.Serialize(webhookDto));

        _mockBusinessIdentificationService
            .Setup(x => x.IdentifyBusinessAsync("entry_id"))
            .ReturnsAsync(businessContext);

        _mockWebhookParserService
            .Setup(x => x.ExtractAllMessagesFromEntryAsync(It.IsAny<Entry>(), It.IsAny<Guid>()))
            .ReturnsAsync(new[] { incomingMessage });

        _mockMessageProcessorService
            .Setup(x => x.ProcessIncomingMessageAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentInboundMetadata?>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _function.Run(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        _mockBusinessIdentificationService.Verify(x => x.IdentifyBusinessAsync("entry_id"), Times.Once);
        _mockWhatsAppService.Verify(x => x.AcknowledgeMessageAsync("entry_id", "test_token", "wamid.test123"), Times.Once);
        _mockWebhookParserService.Verify(x => x.ExtractAllMessagesFromEntryAsync(It.IsAny<Entry>(), businessId), Times.Once);
        _mockMessageProcessorService.Verify(x => x.ProcessIncomingMessageAsync(
            businessId, 
            userNumber, 
            messageText, 
            customerName,
            It.IsAny<AgentInboundMetadata?>()), Times.Once);
        _mockDeduplicationService.Verify(x => x.MarkProcessedAsync(
            businessId,
            "whatsapp",
            It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "wamid.test123" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_PostRequest_WithDuplicateMessage_ShouldNotParseOrProcessMessage()
    {
        // Arrange
        var businessId = Guid.NewGuid();
        var webhookDto = new WhatsAppWebhookDto
        {
            Entry = new List<Entry>
            {
                new Entry
                {
                    Id = "entry_id",
                    Changes = new List<Change>
                    {
                        new Change
                        {
                            Field = "messages",
                            Value = new Value
                            {
                                Metadata = new WebhookMetadata { PhoneNumberId = "entry_id" },
                                Messages = new List<DtoMessage>
                                {
                                    new DtoMessage
                                    {
                                        Id = "wamid.duplicate",
                                        From = "1234567890",
                                        Type = "text",
                                        Text = new TextMessage { Body = "Hola" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var businessContext = new BusinessContext
        {
            BusinessId = businessId,
            BusinessName = "Test Business",
            WhatsAppNumber = new BusinessWhatsAppNumberDto
            {
                WhatsAppPhoneNumberId = "entry_id",
                WhatsAppAccessToken = "test_token"
            }
        };

        var request = CreateMockHttpRequestData("POST", "", JsonSerializer.Serialize(webhookDto));

        _mockBusinessIdentificationService
            .Setup(x => x.IdentifyBusinessAsync("entry_id"))
            .ReturnsAsync(businessContext);

        _mockDeduplicationService
            .Setup(x => x.TryBeginProcessingAsync(
                businessId,
                "whatsapp",
                "wamid.duplicate",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _function.Run(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockWebhookParserService.Verify(
            x => x.ExtractAllMessagesFromEntryAsync(It.IsAny<Entry>(), It.IsAny<Guid>()),
            Times.Never);
        _mockMessageProcessorService.Verify(
            x => x.ProcessIncomingMessageAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<AgentInboundMetadata?>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_PostRequest_WithTalkToHumanIntent_ShouldTransferToHuman()
    {
        // Arrange
        var userNumber = "1234567890";
        var messageText = "Quiero hablar con un humano";
        var businessId = Guid.NewGuid();
        
        var webhookDto = new WhatsAppWebhookDto
        {
            Entry = new List<Entry>
            {
                new Entry
                {
                    Id = "entry_id",
                    Changes = new List<Change>
                    {
                        new Change
                        {
                            Field = "messages",
                            Value = new Value
                            {
                                Metadata = new WebhookMetadata { PhoneNumberId = "entry_id" },
                                Messages = new List<DtoMessage>
                                {
                                    new DtoMessage
                                    {
                                        Id = "wamid.test456",
                                        From = userNumber,
                                        Type = "text",
                                        Text = new TextMessage { Body = messageText }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var businessContext = new BusinessContext
        {
            BusinessId = businessId,
            BusinessName = "Test Business",
            WhatsAppNumber = new BusinessWhatsAppNumberDto
            {
                WhatsAppPhoneNumberId = "entry_id",
                WhatsAppAccessToken = "test_token"
            }
        };

        var incomingMessage = new IncomingMessage
        {
            UserNumber = userNumber,
            MessageText = messageText
        };

        var request = CreateMockHttpRequestData("POST", "", JsonSerializer.Serialize(webhookDto));

        _mockBusinessIdentificationService
            .Setup(x => x.IdentifyBusinessAsync("entry_id"))
            .ReturnsAsync(businessContext);

        _mockWebhookParserService
            .Setup(x => x.ExtractAllMessagesFromEntryAsync(It.IsAny<Entry>(), It.IsAny<Guid>()))
            .ReturnsAsync(new[] { incomingMessage });

        _mockMessageProcessorService
            .Setup(x => x.ProcessIncomingMessageAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentInboundMetadata?>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _function.Run(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        _mockMessageProcessorService.Verify(x => x.ProcessIncomingMessageAsync(
            businessId, 
            userNumber, 
            messageText, 
            null,
            It.IsAny<AgentInboundMetadata?>()), Times.Once);
    }

    [Fact]
    public async Task Run_PostRequest_WithEmptyEntry_ShouldReturnOk()
    {
        // Arrange
        var webhookDto = new WhatsAppWebhookDto
        {
            Entry = new List<Entry>()
        };

        var request = CreateMockHttpRequestData("POST", "", JsonSerializer.Serialize(webhookDto));

        // Act
        var response = await _function.Run(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockMessageProcessorService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_PostRequest_WithException_ShouldReturnInternalServerError()
    {
        // Arrange
        var request = CreateMockHttpRequestData("POST", "", "invalid json");

        // Act
        var response = await _function.Run(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private HttpRequestData CreateMockHttpRequestData(string method, string queryString, string? body = null)
    {
        var context = new Mock<FunctionContext>();
        return new MockHttpRequestData(context.Object, method, queryString, body);
    }
}
