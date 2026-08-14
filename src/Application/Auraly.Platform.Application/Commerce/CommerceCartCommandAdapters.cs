using Auraly.Platform.Application.Agents.Operations.Support;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Planning;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Commerce;

public sealed partial class CommerceCartProductResolver : ICartProductResolver
{
    private readonly ICommerceService _commerce;

    public CommerceCartProductResolver(ICommerceService commerce) => _commerce = commerce;

    public Task<IReadOnlyList<ProductReference>> FindAsync(
        AgentConversationContext context,
        string productText,
        CancellationToken cancellationToken = default)
    {
        var remembered = ProductSelectionMemory.FindCatalogMatches(context, productText);
        return Task.FromResult<IReadOnlyList<ProductReference>>(remembered.Count > 0 ? remembered : []);
    }
}

public sealed partial class CommerceCartMutationStore : ICartMutationStore
{
    private readonly ICommerceService _commerce;
    private readonly IUnitOfWork _unitOfWork;

    public CommerceCartMutationStore(ICommerceService commerce, IUnitOfWork unitOfWork)
    {
        _commerce = commerce;
        _unitOfWork = unitOfWork;
    }

    public Task<OrderSnapshot> GetCurrentAsync(AgentConversationContext context, CancellationToken cancellationToken = default) =>
        _commerce.GetDraftAsync(context, cancellationToken);

    public Task<OrderSnapshot> ApplyAtomicallyAsync(
        AgentConversationContext context,
        IReadOnlyList<ResolvedCartCommand> commands,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            OrderSnapshot? snapshot = null;
            foreach (var command in commands)
            {
                snapshot = command.Operation switch
                {
                    CartCommandOperations.Add => await AddAsync(context, command, cancellationToken),
                    CartCommandOperations.Remove => await _commerce.RemoveItemAsync(context, command.OrderItemId!.Value, cancellationToken),
                    CartCommandOperations.SetQuantity => await _commerce.UpdateItemQuantityAsync(context, command.OrderItemId!.Value, command.Quantity!.Value, cancellationToken),
                    _ => throw new InvalidOperationException($"Unsupported resolved cart operation '{command.Operation}'.")
                };
            }

            return snapshot ?? await _commerce.GetDraftAsync(context, cancellationToken);
        }, cancellationToken);

    private Task<OrderSnapshot> AddAsync(
        AgentConversationContext context,
        ResolvedCartCommand command,
        CancellationToken cancellationToken)
    {
        var product = command.Product!;
        return _commerce.AddItemAsync(
            context,
            new AddOrderItemRequest(
                product.ProductId,
                product.ExternalProductId,
                product.Sku,
                product.Name,
                command.Quantity!.Value,
                product.EffectiveUnitPrice ?? product.UnitPrice),
            cancellationToken);
    }
}
