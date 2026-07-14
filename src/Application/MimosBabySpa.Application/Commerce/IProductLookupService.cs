using MimosBabySpa.Application.Agents;

namespace MimosBabySpa.Application.Commerce;

public interface IProductLookupService
{
    Task<ProductReference?> GetProductAsync(
        AgentConversationContext context,
        ProductLookupRequest request,
        CancellationToken cancellationToken = default);
}
