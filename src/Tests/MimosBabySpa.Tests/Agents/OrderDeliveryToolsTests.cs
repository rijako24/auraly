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

public sealed class OrderDeliveryToolsTests
{
    [Fact]
    public async Task SearchOrder_WhenReplyQuotesAssignment_DoesNotFilterByAcceptText()
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
    public async Task AcceptOrderDelivery_WhenReplyQuotesAssignment_CompletesOrder()
    {
        var fixture = CreateFixture();
        var externalEscalations = new Mock<IExternalEscalationService>();
        externalEscalations
            .Setup(s => s.CompleteAsync(
                fixture.Context.BusinessId,
                fixture.Attempt.ExternalEscalationAttemptId,
                fixture.Context.ChannelPhone,
                "accepted",
                "acepto",
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalEscalationActionResult(
                true,
                fixture.Attempt,
                "Interaccion completada.",
                false,
                "accepted"));

        var tool = new AcceptOrderDeliveryTool(fixture.UnitOfWork.Object, externalEscalations.Object);
        using var args = JsonDocument.Parse("{}");

        var json = await tool.ExecuteAsync(args.RootElement, fixture.Context, CancellationToken.None);

        json.Should().Contain("\"accepted\":true");
        externalEscalations.VerifyAll();
    }

    private static OrderDeliveryFixture CreateFixture()
    {
        var businessId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var phone = "573042052007";
        var replyToMessageId = "wamid.assignment";
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
            ContactRoleSnapshot = "delivery",
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
            DeliveryAssignmentStatus = DeliveryAssignmentStatus.Pending,
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
            .Setup(r => r.GetByWhatsAppMessageIdAsync(
                businessId,
                replyToMessageId,
                phone,
                It.IsAny<CancellationToken>()))
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
            LatestUserMessage = "acepto",
            ReplyToProviderMessageId = replyToMessageId,
            ConversationState = new ConversationStateModel(),
            Conversation = new Conversation()
        };

        return new OrderDeliveryFixture(unitOfWork, ctx, attempt);
    }

    private sealed record OrderDeliveryFixture(
        Mock<IUnitOfWork> UnitOfWork,
        AgentToolContext Context,
        ExternalEscalationAttempt Attempt);
}
