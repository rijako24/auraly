using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CartItemPresentationMemoryTests
{
    [Fact]
    public async Task UpdateAndDecorateAsync_PreservesRequestedNameForFutureSnapshots()
    {
        var productId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var context = Context();
        var snapshot = Snapshot(
            new OrderItemSnapshot(
                itemId,
                productId,
                "PA10",
                "PA10",
                "PAPA FARM FRITES 3/8 X 2.5 KG",
                2m,
                10m,
                20m));
        var command = new ResolvedCartCommand(
            CartCommandOperations.Add,
            new ProductReference(
                productId,
                "PA10",
                "PA10",
                "PAPA FARM FRITES 3/8 X 2.5 KG",
                null,
                null,
                10m,
                "COP",
                20m),
            null,
            2m,
            "papas");

        var decorated = await CartItemPresentationMemory.UpdateAndDecorateAsync(
            null,
            context,
            snapshot,
            [new CartItemPresentationRequest(command, "papas")],
            CancellationToken.None);

        decorated!.Items[0].RequestedName.Should().Be("papas");
        context.Facts.Should().ContainKey(CartItemPresentationMemory.FactKey);

        var later = CartItemPresentationMemory.Decorate(snapshot, context.Facts);
        later.Items[0].RequestedName.Should().Be("papas");
        await CartItemPresentationMemory.UpdateAndDecorateAsync(
            null,
            context,
            Snapshot(),
            [],
            CancellationToken.None);
        context.Facts.Should().NotContainKey(CartItemPresentationMemory.FactKey);
    }

    [Fact]
    public async Task UpdateAndDecorateAsync_DoesNotDuplicateExactNameAndPrunesRemovedItems()
    {
        var productId = Guid.NewGuid();
        var context = Context();
        var productName = "SALCHICHA RANCHERA SUPER";
        var snapshot = Snapshot(
            new OrderItemSnapshot(
                Guid.NewGuid(), productId, "CF2", "CF2", productName, 1m, 10m, 10m));
        var command = new ResolvedCartCommand(
            CartCommandOperations.Add,
            new ProductReference(
                productId, "CF2", "CF2", productName, null, null, 10m, "COP", 10m),
            null,
            1m,
            productName);

        var decorated = await CartItemPresentationMemory.UpdateAndDecorateAsync(
            null,
            context,
            snapshot,
            [new CartItemPresentationRequest(command, productName)],
            CancellationToken.None);

        decorated!.Items[0].RequestedName.Should().BeNull();
        context.Facts.Should().NotContainKey(CartItemPresentationMemory.FactKey);

        await CartItemPresentationMemory.UpdateAndDecorateAsync(
            null,
            context,
            Snapshot(),
            [],
            CancellationToken.None);
        context.Facts.Should().NotContainKey(CartItemPresentationMemory.FactKey);
    }

    private static AgentConversationContext Context() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new()
    };

    private static OrderSnapshot Snapshot(params OrderItemSnapshot[] items) =>
        new(Guid.NewGuid(), OrderStatus.Draft, "COP", 20m, 0m, 20m, items);
}
