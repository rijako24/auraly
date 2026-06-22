using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MimosBabySpa.API.Functions;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Tests.Helpers;
using Xunit;
using DtoMessage = MimosBabySpa.Application.DTOs.Message;

namespace MimosBabySpa.Tests.Functions;

public class WhatsAppWebhookFunctionTests
{
    private readonly Mock<IWhatsAppMessageProcessorService> _mockMessageProcessorService;
    private readonly Mock<IWhatsAppService> _mockWhatsAppService;
    private readonly Mock<IBusinessIdentificationService> _mockBusinessIdentificationService;
    private readonly Mock<IInboundMessageDeduplicationService> _mockDeduplicationService;
    private readonly Mock<IWhatsAppInboundQueueService> _mockQueueService;
    private readonly Mock<ILogger<WhatsAppWebhookFunction>> _mockLogger;
    private readonly WhatsAppWebhookFunction _function;

    public WhatsAppWebhookFunctionTests()
    {
        _mockMessageProcessorService = new Mock<IWhatsAppMessageProcessorService>();
        _mockWhatsAppService = new Mock<IWhatsAppService>();
        _mockBusinessIdentificationService = new Mock<IBusinessIdentificationService>();
        _mockDeduplicationService = new Mock<IInboundMessageDeduplicationService>();
        _mockQueueService = new Mock<IWhatsAppInboundQueueService>();
        _mockLogger = new Mock<ILogger<WhatsAppWebhookFunction>>();

        _mockWhatsAppService
            .Setup(x => x.AcknowledgeMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockDeduplicationService
            .Setup(x => x.TryRecordReceivedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockDeduplicationService
            .Setup(x => x.MarkQueuedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockQueueService
            .Setup(x => x.ScheduleDebounceAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _function = new WhatsAppWebhookFunction(
            _mockMessageProcessorService.Object,
            _mockWhatsAppService.Object,
            _mockBusinessIdentificationService.Object,
            _mockDeduplicationService.Object,
            _mockQueueService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Run_GetRequest_WithValidWebhookVerification_ShouldReturnChallenge()
    {
        var challenge = "test_challenge_123";
        var request = CreateMockHttpRequestData("GET", $"?hub.mode=subscribe&hub.verify_token=test_token&hub.challenge={challenge}");

        _mockMessageProcessorService
            .Setup(x => x.VerifyWebhookAsync("subscribe", "test_token", challenge))
            .ReturnsAsync(challenge);

        var response = await _function.Run(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockMessageProcessorService.Verify(x => x.VerifyWebhookAsync("subscribe", "test_token", challenge), Times.Once);
    }

    [Fact]
    public async Task Run_GetRequest_WithInvalidWebhookVerification_ShouldReturnForbidden()
    {
        var request = CreateMockHttpRequestData("GET", "?hub.mode=subscribe&hub.verify_token=wrong_token&hub.challenge=test");

        _mockMessageProcessorService
            .Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var response = await _function.Run(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Run_PostRequest_WithValidMessage_ShouldPersistAndScheduleDebounce()
    {
        var userNumber = "1234567890";
        var customerName = "Juan Perez";
        var businessId = Guid.NewGuid();
        var webhookDto = CreateWebhookDto("wamid.test123", userNumber, "Hola", customerName);
        var request = CreateMockHttpRequestData("POST", "", JsonSerializer.Serialize(webhookDto));

        _mockBusinessIdentificationService
            .Setup(x => x.IdentifyBusinessAsync("entry_id"))
            .ReturnsAsync(CreateBusinessContext(businessId));

        var response = await _function.Run(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockWhatsAppService.Verify(x => x.AcknowledgeMessageAsync("entry_id", "test_token", "wamid.test123"), Times.Once);
        _mockDeduplicationService.Verify(x => x.TryRecordReceivedAsync(
            businessId,
            "whatsapp",
            "wamid.test123",
            userNumber,
            customerName,
            It.Is<string>(json => json.Contains("wamid.test123")),
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockQueueService.Verify(x => x.ScheduleDebounceAsync(
            businessId,
            "whatsapp",
            userNumber,
            "wamid.test123",
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockMessageProcessorService.Verify(x => x.ProcessIncomingMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<MimosBabySpa.Application.Agents.AgentInboundMetadata?>()), Times.Never);
    }

    [Fact]
    public async Task Run_PostRequest_WithDuplicateMessage_ShouldStillScheduleWakeupButNotInsertAgain()
    {
        var businessId = Guid.NewGuid();
        var webhookDto = CreateWebhookDto("wamid.duplicate", "1234567890", "Hola", null);
        var request = CreateMockHttpRequestData("POST", "", JsonSerializer.Serialize(webhookDto));

        _mockBusinessIdentificationService
            .Setup(x => x.IdentifyBusinessAsync("entry_id"))
            .ReturnsAsync(CreateBusinessContext(businessId));

        _mockDeduplicationService
            .Setup(x => x.TryRecordReceivedAsync(
                businessId,
                "whatsapp",
                "wamid.duplicate",
                "1234567890",
                null,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var response = await _function.Run(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockQueueService.Verify(x => x.ScheduleDebounceAsync(
            businessId,
            "whatsapp",
            "1234567890",
            "wamid.duplicate",
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockMessageProcessorService.Verify(x => x.ProcessIncomingMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<MimosBabySpa.Application.Agents.AgentInboundMetadata?>()), Times.Never);
    }

    [Fact]
    public async Task Run_PostRequest_WithEmptyEntry_ShouldReturnOk()
    {
        var webhookDto = new WhatsAppWebhookDto { Entry = [] };
        var request = CreateMockHttpRequestData("POST", "", JsonSerializer.Serialize(webhookDto));

        var response = await _function.Run(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockMessageProcessorService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_PostRequest_WithException_ShouldReturnInternalServerError()
    {
        var request = CreateMockHttpRequestData("POST", "", "invalid json");

        var response = await _function.Run(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private static WhatsAppWebhookDto CreateWebhookDto(
        string messageId,
        string userNumber,
        string messageText,
        string? customerName)
    {
        return new WhatsAppWebhookDto
        {
            Entry =
            [
                new Entry
                {
                    Id = "entry_id",
                    Changes =
                    [
                        new Change
                        {
                            Field = "messages",
                            Value = new Value
                            {
                                Metadata = new WebhookMetadata { PhoneNumberId = "entry_id" },
                                Messages =
                                [
                                    new DtoMessage
                                    {
                                        Id = messageId,
                                        From = userNumber,
                                        Type = "text",
                                        Text = new TextMessage { Body = messageText }
                                    }
                                ],
                                Contacts = customerName is null
                                    ? []
                                    :
                                    [
                                        new Contact
                                        {
                                            Profile = new Profile { Name = customerName }
                                        }
                                    ]
                            }
                        }
                    ]
                }
            ]
        };
    }

    private static BusinessContext CreateBusinessContext(Guid businessId)
    {
        return new BusinessContext
        {
            BusinessId = businessId,
            BusinessName = "Test Business",
            WhatsAppNumber = new BusinessWhatsAppNumberDto
            {
                WhatsAppPhoneNumberId = "entry_id",
                WhatsAppAccessToken = "test_token"
            }
        };
    }

    private HttpRequestData CreateMockHttpRequestData(string method, string queryString, string? body = null)
    {
        var context = new Mock<FunctionContext>();
        return new MockHttpRequestData(context.Object, method, queryString, body);
    }
}
