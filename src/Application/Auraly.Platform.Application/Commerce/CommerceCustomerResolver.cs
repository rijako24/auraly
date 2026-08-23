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

public interface ICanonicalCommerceCustomerLookup
{
    Task<CommerceCustomerReference?> FindAsync(
        Guid businessId,
        Guid integrationConnectionId,
        CommerceProvider provider,
        string phone,
        CancellationToken ct = default);
}

public sealed class CommerceCustomerResolver : ICommerceCustomerResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICanonicalCommerceCustomerLookup _customers;

    public CommerceCustomerResolver(
        IUnitOfWork unitOfWork,
        ICanonicalCommerceCustomerLookup customers)
    {
        _unitOfWork = unitOfWork;
        _customers = customers;
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

        return await _customers.FindAsync(
            businessId,
            connection.IntegrationConnectionId,
            config.Commerce.Provider,
            phone,
            ct);
    }
}
