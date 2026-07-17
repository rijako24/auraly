using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Commerce;

public sealed record CommerceAdapterContext(
    Guid BusinessId,
    Guid AgentId,
    Guid? ConversationId,
    CommerceProvider Provider,
    IntegrationConnection? Connection,
    string? CustomerPhone = null,
    CommerceCustomerReference? Customer = null,
    string? WarehouseCode = null);

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
