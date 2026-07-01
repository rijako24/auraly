using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class OrderEscalationToolsTests
{
    [Fact]
    public async Task SearchOrder_WhenReplyQuotesRequest_DoesNotFilterByAcceptText()
    {
        var fixture = CreateFixture();
        var tool = new SearchOrderTool(fixture.UnitOfWork.Object);
        using var args = JsonDocument.Parse("""{"query":"acepto"}""");

        var json = await tool.ExecuteAsync(args.RootElement, fixture.Context, CancellationToken.None);

        using var result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(1);
        json.Should().Contain("ORD-9001");
    }

    [Fact]
    public async Task AcceptOrderRequest_WhenReplyQuotesRequest_CompletesEscalationWithAcceptedOutcome()
    {
        var fixture = CreateFixture();
        var escalations = CreateEscalationsMock(fixture, "accepted", ExternalEscalationAttemptStatus.Accepted);
        var tool = new AcceptOrderRequestTool(fixture.UnitOfWork.Object, escalations.Object);
        using var args = JsonDocument.Parse("{}");

        var json = await tool.ExecuteAsync(args.RootElement, fixture.Context, CancellationToken.None);

        json.Should().Contain("\"accepted\":true");
        escalations.Verify(s => s.CompleteAttemptAsync(
            It.Is<ExternalEscalationCompletionRequest>(r =>
                r.BusinessId == fixture.Context.BusinessId &&
                r.AttemptId == fixture.Attempt.ExternalEscalationAttemptId &&
                r.ContactPhone == fixture.Context.ChannelPhone &&
                r.OutcomeKey == "accepted" &&
                r.CompletedStatus == ExternalEscalationAttemptStatus.Accepted &&
                r.Payload!["order_number"] == "ORD-9001"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptOrderRequest_WhenInteractivePayloadIsAccepted_CompletesEscalation()
    {
        var fixture = CreateFixture(interactivePayloadOutcome: "accepted");
        var escalations = CreateEscalationsMock(fixture, "accepted", ExternalEscalationAttemptStatus.Accepted);
        var tool = new AcceptOrderRequestTool(fixture.UnitOfWork.Object, escalations.Object);
        using var args = JsonDocument.Parse("{}");

        var json = await tool.ExecuteAsync(args.RootElement, fixture.Context, CancellationToken.None);

        json.Should().Contain("\"accepted\":true");
        escalations.Verify(s => s.CompleteAttemptAsync(It.IsAny<ExternalEscalationCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptOrderRequest_WhenInteractivePayloadIsDeclined_DoesNotComplete()
    {
        var fixture = CreateFixture(interactivePayloadOutcome: "declined", latestUserMessage: "rechazo");
        var escalations = new Mock<IExternalEscalationService>(MockBehavior.Strict);
        var tool = new AcceptOrderRequestTool(fixture.UnitOfWork.Object, escalations.Object);
        using var args = JsonDocument.Parse("{}");

        var json = await tool.ExecuteAsync(args.RootElement, fixture.Context, CancellationToken.None);

        using var result = JsonDocument.Parse(json);
        var data = result.RootElement.GetProperty("data");
        data.GetProperty("accepted").GetBoolean().Should().BeFalse();
        data.GetProperty("reason").GetString().Should().Be("outcome_mismatch");
        escalations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RejectOrderRequest_WhenReplyQuotesRequest_CompletesEscalationWithDeclinedOutcome()
    {
        var fixture = CreateFixture(latestUserMessage: "rechazo");
        var escalations = CreateEscalationsMock(fixture, "declined", ExternalEscalationAttemptStatus.Declined);
        var tool = new RejectOrderRequestTool(fixture.UnitOfWork.Object, escalations.Object);
        using var args = JsonDocument.Parse("{}");

        var json = await tool.ExecuteAsync(args.RootElement, fixture.Context, CancellationToken.None);

        json.Should().Contain("\"rejected\":true");
        escalations.Verify(s => s.CompleteAttemptAsync(
            It.Is<ExternalEscalationCompletionRequest>(r =>
                r.BusinessId == fixture.Context.BusinessId &&
                r.AttemptId == fixture.Attempt.ExternalEscalationAttemptId &&
                r.ContactPhone == fixture.Context.ChannelPhone &&
                r.OutcomeKey == "declined" &&
                r.CompletedStatus == ExternalEscalationAttemptStatus.Declined &&
                r.Payload!["order_id"] == fixture.Attempt.TargetId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectOrderRequest_WhenInteractivePayloadIsDeclined_CompletesEscalation()
    {
        var fixture = CreateFixture(interactivePayloadOutcome: "declined", latestUserMessage: "rechazo");
        var escalations = CreateEscalationsMock(fixture, "declined", ExternalEscalationAttemptStatus.Declined);
        var tool = new RejectOrderRequestTool(fixture.UnitOfWork.Object, escalations.Object);
        using var args = JsonDocument.Parse("{}");

        var json = await tool.ExecuteAsync(args.RootElement, fixture.Context, CancellationToken.None);

        json.Should().Contain("\"rejected\":true");
        escalations.Verify(s => s.CompleteAttemptAsync(It.IsAny<ExternalEscalationCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectOrderRequest_WhenInteractivePayloadIsAccepted_DoesNotComplete()
    {
        var fixture = CreateFixture(interactivePayloadOutcome: "accepted");
        var escalations = new Mock<IExternalEscalationService>(MockBehavior.Strict);
        var tool = new RejectOrderRequestTool(fixture.UnitOfWork.Object, escalations.Object);
        using var args = JsonDocument.Parse("{}");

        var json = await tool.ExecuteAsync(args.RootElement, fixture.Context, CancellationToken.None);

        using var result = JsonDocument.Parse(json);
        var data = result.RootElement.GetProperty("data");
        data.GetProperty("rejected").GetBoolean().Should().BeFalse();
        data.GetProperty("reason").GetString().Should().Be("outcome_mismatch");
        escalations.VerifyNoOtherCalls();
    }

    private static Mock<IExternalEscalationService> CreateEscalationsMock(
        OrderEscalationFixture fixture,
        string outcomeKey,
        ExternalEscalationAttemptStatus status)
    {
        var escalations = new Mock<IExternalEscalationService>();
        escalations
            .Setup(s => s.CompleteAttemptAsync(
                It.Is<ExternalEscalationCompletionRequest>(r => r.OutcomeKey == outcomeKey && r.CompletedStatus == status),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalEscalationCompletionRequest request, CancellationToken _) =>
            {
                fixture.Attempt.Status = request.CompletedStatus;
                fixture.Attempt.OutcomeKey = request.OutcomeKey;
                fixture.Attempt.CompletedAt = DateTime.UtcNow;
                return new ExternalEscalationCompletionResult(
                    true,
                    fixture.Attempt,
                    "Escalacion completada.",
                    request.OutcomeKey,
                    request.Payload);
            });
        return escalations;
    }

    private static OrderEscalationFixture CreateFixture(string? interactivePayloadOutcome = null, string latestUserMessage = "acepto")
    {
        var businessId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var phone = "573042052007";
        var replyToMessageId = "wamid.request";
        var attempt = new ExternalEscalationAttempt
        {
            ExternalEscalationAttemptId = Guid.NewGuid(),
            BusinessId = businessId,
            SourceAgentId = Guid.NewGuid(),
            EventName = "order_created",
            TargetType = "order",
            TargetId = orderId,
            ContactKey = "domicilio_solorzano",
            ContactNameSnapshot = "Domicilio Solorzano",
            ContactRoleSnapshot = "domicilio",
            ContactPhoneSnapshot = phone,
            AttemptCode = "PED-9001",
            CustomPayloadJson = """{"order_number":"ORD-9001","city":"Bucaramanga"}""",
            WhatsAppMessageId = replyToMessageId,
            Status = ExternalEscalationAttemptStatus.Pending,
            EscalatedAt = DateTime.UtcNow.AddMinutes(-2),
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        var order = new Order
        {
            OrderId = orderId,
            BusinessId = businessId,
            CustomerNameSnapshot = "Cliente Solorzano",
            CustomerPhoneSnapshot = "3001234567",
            DeliveryAddressSnapshot = "Calle 10 #20-30",
            Total = 90000m,
            Currency = "COP"
        };
        var items = new List<OrderItem>
        {
            new()
            {
                OrderItemId = Guid.NewGuid(),
                BusinessId = businessId,
                OrderId = orderId,
                ProductNameSnapshot = "Vino artesanal",
                Quantity = 1,
                LineTotal = 90000m
            }
        };

        var attempts = new Mock<IExternalEscalationAttemptRepository>();
        attempts
            .Setup(r => r.GetByIdAsync(attempt.ExternalEscalationAttemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);
        attempts
            .Setup(r => r.GetByWhatsAppMessageIdAsync(businessId, replyToMessageId, phone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);

        var orders = new Mock<IOrderRepository>();
        orders
            .Setup(r => r.GetByIdAsync(businessId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var orderItems = new Mock<IOrderItemRepository>();
        orderItems
            .Setup(r => r.GetByOrderIdAsync(businessId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.ExternalEscalationAttempts).Returns(attempts.Object);
        unitOfWork.SetupGet(u => u.Orders).Returns(orders.Object);
        unitOfWork.SetupGet(u => u.OrderItems).Returns(orderItems.Object);

        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationId = Guid.NewGuid(),
            ChannelPhone = phone,
            LatestUserMessage = latestUserMessage,
            ReplyToProviderMessageId = interactivePayloadOutcome is null ? replyToMessageId : null,
            InteractivePayload = interactivePayloadOutcome is null
                ? null
                : $"external_interaction:{interactivePayloadOutcome}:{attempt.ExternalEscalationAttemptId:N}",
            ConversationState = new ConversationStateModel(),
            Conversation = new Conversation()
        };

        return new OrderEscalationFixture(unitOfWork, ctx, attempt, order);
    }

    private sealed record OrderEscalationFixture(
        Mock<IUnitOfWork> UnitOfWork,
        AgentToolContext Context,
        ExternalEscalationAttempt Attempt,
        Order Order);
}


