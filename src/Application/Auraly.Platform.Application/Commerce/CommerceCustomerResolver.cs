using Auraly.Platform.Application.Agents;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Commerce;

public interface ICommerceCustomerResolver
{
    Task<CommerceCustomerReference?> ResolveAsync(
        Guid businessId,
        Guid agentId,
        Guid conversationId,
        string phone,
        AgentConfig config,
        CancellationToken ct = default);
}

public sealed class CommerceCustomerResolver : ICommerceCustomerResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICommerceAdapterFactory _adapters;

    public CommerceCustomerResolver(
        IUnitOfWork unitOfWork,
        ICommerceAdapterFactory adapters)
    {
        _unitOfWork = unitOfWork;
        _adapters = adapters;
    }

    public async Task<CommerceCustomerReference?> ResolveAsync(
        Guid businessId,
        Guid agentId,
        Guid conversationId,
        string phone,
        AgentConfig config,
        CancellationToken ct = default)
    {
        if (!config.Commerce.Enabled
            || config.Commerce.Provider == CommerceProvider.Local
            || string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            businessId,
            config.Commerce.Provider,
            CommerceCapability.CatalogAndOrders,
            ct);
        if (connection is null || !connection.IsEnabled)
            return null;

        var adapter = _adapters.Resolve(config.Commerce.Provider);
        if (adapter is not ICommerceCustomerLookup customerLookup)
            return null;

        var context = new CommerceAdapterContext(
            businessId,
            agentId,
            conversationId,
            config.Commerce.Provider,
            connection,
            phone);
        return await customerLookup.FindCustomerAsync(context, ct);
    }
}
