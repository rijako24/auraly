using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public sealed record ExternalCommerceCustomerKey(string ExternalAccountId, string ExternalCustomerId);

public interface IExternalCommerceCustomerRepository
{
    Task<IReadOnlyList<ExternalCommerceCustomer>> FindActiveByPhoneAsync(
        Guid businessId,
        Guid integrationConnectionId,
        string phoneNormalized,
        CancellationToken ct = default);

    Task<ExternalCommerceCustomer?> GetByExternalKeysAsync(
        Guid businessId,
        Guid integrationConnectionId,
        string externalAccountId,
        string externalCustomerId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExternalCommerceCustomer>> GetByExternalKeysAsync(
        Guid businessId,
        Guid integrationConnectionId,
        IReadOnlyCollection<ExternalCommerceCustomerKey> keys,
        CancellationToken ct = default);

    Task<ExternalCommerceCustomer> CreateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default);

    Task CreateManyAsync(IReadOnlyCollection<ExternalCommerceCustomer> customers, CancellationToken ct = default);

    Task<ExternalCommerceCustomer> UpdateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default);

    Task UpdateManyAsync(IReadOnlyCollection<ExternalCommerceCustomer> customers, CancellationToken ct = default);
}
