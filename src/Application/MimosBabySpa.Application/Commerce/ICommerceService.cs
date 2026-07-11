using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Commerce;

public interface ICommerceService
{
    Task<ProductSearchResult> SearchProductsAsync(AgentConversationContext ctx, ProductSearchRequest request, CancellationToken ct = default);
    Task<OrderSnapshot> AddItemAsync(AgentConversationContext ctx, AddOrderItemRequest request, CancellationToken ct = default);
    Task<OrderSnapshot> RemoveItemAsync(AgentConversationContext ctx, Guid orderItemId, CancellationToken ct = default);
    Task<OrderSnapshot> UpdateItemQuantityAsync(AgentConversationContext ctx, Guid orderItemId, decimal quantity, CancellationToken ct = default);
    Task<OrderSnapshot> GetDraftAsync(AgentConversationContext ctx, CancellationToken ct = default);
    Task<OrderSnapshot> CreateOrderAsync(AgentConversationContext ctx, CreateOrderRequest request, CancellationToken ct = default);
    Task<OrderSnapshot> ConfirmPaidOrderAsync(Guid businessId, Guid paymentTransactionId, AgentConfig config, CancellationToken ct = default);
}
