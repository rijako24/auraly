using Auraly.Platform.Application.Agents;

namespace Auraly.Platform.Application.Commerce;

public interface IProductLookupService
{
    Task<ProductReference?> GetProductAsync(
        AgentConversationContext context,
        ProductLookupRequest request,
        CancellationToken cancellationToken = default);
}
