using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ResetFlowContextToolTests
{
    [Fact]
    public async Task ExecuteAsync_ClearsNonPersistentFactsAndAbandonsActiveCheckout()
    {
        var conversationId = Guid.NewGuid();
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            ConversationId = conversationId,
            Status = PaymentTransactionStatus.Created
        };

        var facts = new Mock<IConversationFactsService>();
        facts.Setup(f => f.ClearNonPersistentAsync(
                conversationId,
                It.Is<IReadOnlyCollection<string>>(keys => keys.Contains("customer_name")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["service", "desired_date"]);

        var payments = new Mock<IPaymentLifecycleService>();
        payments.Setup(p => p.GetActiveByConversationAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        payments.Setup(p => p.MarkAbandonedAsync(payment, It.IsAny<CancellationToken>()))
            .Callback(() => payment.Status = PaymentTransactionStatus.Abandoned)
            .Returns(Task.CompletedTask);

        var tool = new ResetFlowContextTool(facts.Object, payments.Object);
        var ctx = new AgentToolContext
        {
            ConversationId = conversationId,
            ActivePayment = payment,
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "customer_name", PersistsAcrossConversations = true },
                    new FactSchemaEntry { Key = "service", PersistsAcrossConversations = false }
                ]
            },
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_name"] = "Ana",
                ["service"] = "Plan Marineritos",
                ["desired_date"] = "2026-06-10"
            },
            ConversationState = new ConversationStateModel()
        };
        ctx.ConversationState.Verifications[VerificationFactTypes.CheckoutPrepared] =
            new(DateTime.UtcNow, null, "{}");
        ctx.ConversationState.StageFactSnapshots["scheduling"] =
            new Dictionary<string, string> { ["service"] = "Plan Marineritos" };
        ctx.ConversationState.CompletedOneShotStages.Add("legacy_stage");

        using var args = JsonDocument.Parse("""{"reason":"start_new_request","checkout_action":"abandon"}""");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        payment.Status.Should().Be(PaymentTransactionStatus.Abandoned);
        ctx.ActivePayment.Should().BeNull();
        ctx.Facts.Should().Contain("customer_name", "Ana");
        ctx.Facts.Should().NotContainKey("service");
        ctx.Facts.Should().NotContainKey("desired_date");
        ctx.ConversationState.Verifications.Should().BeEmpty();
        ctx.ConversationState.StageFactSnapshots.Should().BeEmpty();
        ctx.ConversationState.CompletedOneShotStages.Should().BeEmpty();
    }
}
