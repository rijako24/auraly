using MimosBabySpa.Application.Agents;

namespace MimosBabySpa.Application.Commerce;

public interface ICommerceService
{
    Task<ProductSearchResult> SearchProductsAsync(AgentToolContext ctx, ProductSearchRequest request, CancellationToken ct = default);
    Task<OrderSnapshot> AddItemAsync(AgentToolContext ctx, AddOrderItemRequest request, CancellationToken ct = default);
    Task<OrderSnapshot> RemoveItemAsync(AgentToolContext ctx, Guid orderItemId, CancellationToken ct = default);
    Task<OrderSnapshot> UpdateItemQuantityAsync(AgentToolContext ctx, Guid orderItemId, decimal quantity, CancellationToken ct = default);
    Task<OrderSnapshot> GetDraftAsync(AgentToolContext ctx, CancellationToken ct = default);
    Task<OrderSnapshot> CreateOrderAsync(AgentToolContext ctx, CreateOrderRequest request, CancellationToken ct = default);
}
