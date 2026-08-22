using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Commerce;

public sealed record CommerceAdapterContext(
    Guid BusinessId,
    Guid AgentId,
    Guid? ConversationId,
    CommerceProvider Provider,
    IntegrationConnection? Connection,
    string? CustomerPhone = null,
    CommerceCustomerReference? Customer = null,
    string? WarehouseCode = null,
    Guid? WarehouseId = null);

public sealed record CommerceOrderWorkspace(Guid WarehouseId, string WarehouseCode);

public interface ICommerceOrderWorkspaceResolver
{
    Task<CommerceOrderWorkspace?> ResolveAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);
}

public sealed record CommerceCustomerReference(
    CommerceProvider Provider,
    string ExternalAccountId,
    string ExternalCustomerId,
    string? Name,
    string Phone);

public interface ICommerceCustomerLookup
{
    Task<CommerceCustomerReference?> FindCustomerAsync(
        CommerceAdapterContext context,
        CancellationToken ct = default);
}
