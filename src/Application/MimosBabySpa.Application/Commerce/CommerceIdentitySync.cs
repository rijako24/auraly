namespace MimosBabySpa.Application.Commerce;

public sealed record ProductIdentityPage(
    IReadOnlyList<ProductReference> Products,
    bool HasMore);

public interface ICommerceProductIdentitySource
{
    Task<ProductIdentityPage> GetProductIdentityPageAsync(
        CommerceAdapterContext context,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

public interface ICommerceProductDeltaIdentitySource
{
    Task<ProductIdentityPage> GetProductIdentityDeltaPageAsync(
        CommerceAdapterContext context,
        DateTime changedOnUtc,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

public sealed record ExternalCustomerIdentityReference(
    string ExternalAccountId,
    string ExternalCustomerId,
    string? Name,
    string PhoneNormalized,
    string? Phone);

public sealed record ExternalCustomerIdentityPage(
    IReadOnlyList<ExternalCustomerIdentityReference> Customers,
    bool HasMore);

public interface ICommerceCustomerIdentitySource
{
    Task<ExternalCustomerIdentityPage> GetCustomerIdentityPageAsync(
        CommerceAdapterContext context,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
