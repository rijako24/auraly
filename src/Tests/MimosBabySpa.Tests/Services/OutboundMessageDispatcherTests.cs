using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Billing;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class OutboundMessageDispatcherTests
{
    private readonly Mock<IWhatsAppService> _whatsApp = new();
    private readonly Mock<IMessageService> _messages = new();
    private readonly Mock<IUsageBillingService> _billing = new();

    public OutboundMessageDispatcherTests()
    {
        _billing
            .Setup(b => b.CanProcessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageGateResult(true, "ok", "ok", null));

        _billing
            .Setup(b => b.ChargeAsync(It.IsAny<UsageChargeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageChargeResult(true, 1, 0, null));
    }

    [Fact]
    public async Task SendAllAsync_WithConversationId_SavesOutboundMessagesInHistory()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var dispatcher = CreateDispatcher();
        var outbound = new[]
        {
            new OutboundMessage("Reserva confirmada", null),
            new OutboundMessage("Te adjunto la guia", "https://cdn.example.com/guia.pdf", "document", "guia.pdf")
        };

        await dispatcher.SendAllAsync(businessId, "573001112233", outbound, conversationId);

        _messages.Verify(m => m.SaveMessageAsync(conversationId, "bot", "Reserva confirmada"), Times.Once);
        _messages.Verify(m => m.SaveMessageAsync(
            conversationId,
            "bot",
            It.Is<string>(text =>
                text.Contains("Te adjunto la guia") &&
                text.Contains("[document] guia.pdf - https://cdn.example.com/guia.pdf"))), Times.Once);

        _billing.Verify(b => b.ChargeAsync(
            It.Is<UsageChargeRequest>(r =>
                r.BusinessId == businessId &&
                r.ConversationId == conversationId &&
                r.OperationType == UsageOperationType.OutboundSequence &&
                r.OutboundMessages == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAllAsync_WithoutConversationId_DoesNotWriteHistory()
    {
        var dispatcher = CreateDispatcher();

        await dispatcher.SendAllAsync(
            Guid.NewGuid(),
            "573001112233",
            [new OutboundMessage("Mensaje suelto", null)]);

        _messages.Verify(m => m.SaveMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendAllAsync_ChargesUsageForDemoRequestOutbound()
    {
        var businessId = Guid.NewGuid();
        var dispatcher = CreateDispatcher();

        await dispatcher.SendAllAsync(
            businessId,
            "573001112233",
            [new OutboundMessage("Solicitud de demo", null)]);

        _billing.Verify(b => b.CanProcessAsync(
            businessId,
            It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.ChargeAsync(
            It.Is<UsageChargeRequest>(r =>
                r.BusinessId == businessId &&
                r.OperationType == UsageOperationType.OutboundSequence &&
                r.OutboundMessages == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        _whatsApp.Verify(w => w.SendTextMessageAsync(
            businessId,
            "573001112233",
            "Solicitud de demo"), Times.Once);
    }

    private OutboundMessageDispatcher CreateDispatcher() =>
        new(
            _whatsApp.Object,
            _messages.Object,
            _billing.Object,
            NullLogger<OutboundMessageDispatcher>.Instance);
}
