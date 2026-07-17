using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

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

    Task<ExternalCommerceCustomer> CreateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default);

    Task<ExternalCommerceCustomer> UpdateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default);
}
