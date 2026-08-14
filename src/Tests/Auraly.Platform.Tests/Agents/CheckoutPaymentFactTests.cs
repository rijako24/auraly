using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Facts;
using Auraly.Platform.Application.Agents.Operations.Support;
using Auraly.Platform.Application.Services;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class CheckoutPaymentFactTests
{
    [Fact]
    public void Get_WhenPaymentMethodFactIsNotDeclared_IgnoresFallbackFact()
    {
        var roles = new FactRoleIndex([]);
        var ctx = new AgentConversationContext
        {
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["payment_method"] = "transferencia"
            }
        };

        var result = CheckoutPaymentFact.Get(ctx, roles);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PersistSelectionAsync_WhenPaymentMethodFactIsNotDeclared_DoesNotWriteFallbackFact()
    {
        var facts = new Mock<IConversationFactsService>();
        var roles = new FactRoleIndex([]);
        var ctx = new AgentConversationContext
        {
            ConversationId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            Facts = []
        };
        var selection = new CheckoutPaymentSelection(
            MissingPaymentMethod: false,
            Error: null,
            MethodKey: "transferencia",
            MethodLabel: "transferencia",
            PaymentPercentage: 100,
            PayableCents: 25_000,
            TemplateId: "checkout_with_payment",
            ConfirmationOutcome: "paid");

        await CheckoutPaymentFact.PersistSelectionAsync(facts.Object, ctx, roles, selection, CancellationToken.None);

        ctx.Facts.Should().NotContainKey("payment_method");
        facts.Verify(f => f.SetAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Get_WhenPaymentMethodFallbackKeyIsDeclared_ReadsFact()
    {
        var roles = new FactRoleIndex([
            new FactSchemaEntry { Key = "payment_method", Role = "payment.method", Source = "user" }
        ]);
        var ctx = new AgentConversationContext
        {
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["payment_method"] = "transferencia"
            }
        };

        var result = CheckoutPaymentFact.Get(ctx, roles);

        result.Should().Be("transferencia");
    }
}
