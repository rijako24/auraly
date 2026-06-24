using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class RemoveOrderItemToolTests
{
    private readonly Mock<ICommerceService> _commerce = new();
    private readonly Mock<IConversationFactsService> _facts = new();

    [Fact]
    public async Task ExecuteAsync_WithPartialName_DoesNotMatchDulceInsideSemidulce()
    {
        var ctx = CreateContext();
        var semidulceItemId = Guid.NewGuid();
        var dulceItemId = Guid.NewGuid();
        var draft = WineSnapshot(semidulceItemId, dulceItemId);
        var updated = WineSnapshot(semidulceItemId, dulceItemId, includeDulce: false);

        _commerce
            .Setup(c => c.GetDraftAsync(ctx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        _commerce
            .Setup(c => c.RemoveItemAsync(ctx, dulceItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);
        _facts
            .Setup(f => f.ClearFieldsAsync(ctx.ConversationId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new RemoveOrderItemTool(_commerce.Object, _facts.Object);
        using var args = JsonDocument.Parse("""{"name":"Vino Dulce 750 ml"}""");

        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        _commerce.Verify(c => c.RemoveItemAsync(ctx, dulceItemId, It.IsAny<CancellationToken>()), Times.Once);
        _commerce.Verify(c => c.RemoveItemAsync(ctx, semidulceItemId, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static OrderSnapshot WineSnapshot(Guid semidulceItemId, Guid dulceItemId, bool includeDulce = true)
    {
        var semidulceProductId = Guid.NewGuid();
        var dulceProductId = Guid.NewGuid();
        var items = new List<OrderItemSnapshot>
        {
            new(semidulceItemId, semidulceProductId, null, null, "Vino Semidulce 750 ml", 1m, 60000m, 60000m)
        };

        if (includeDulce)
            items.Add(new(dulceItemId, dulceProductId, null, null, "Vino Dulce 750 ml en base de Corozo", 1m, 60000m, 60000m));

        var total = items.Sum(i => i.LineTotal);
        return new(Guid.NewGuid(), OrderStatus.Draft, "COP", total, 0m, 0m, total, items);
    }

    private static AgentToolContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}
