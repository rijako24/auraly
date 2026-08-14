using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IProductCategoryRepository
{
    Task<ProductCategory?> GetByIdAsync(Guid businessId, Guid productCategoryId, CancellationToken ct = default);

    Task<IReadOnlyList<ProductCategory>> ListAsync(Guid businessId, bool includeInactive, CancellationToken ct = default);

    Task<ProductCategory?> GetByExternalIdAsync(
        Guid businessId,
        Guid integrationConnectionId,
        string externalCategoryId,
        CancellationToken ct = default);

    Task<ProductCategory?> GetByNameAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        string name,
        CancellationToken ct = default);

    Task<ProductCategory?> FindBrowsableByNameAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        string name,
        CancellationToken ct = default);

    Task<(IReadOnlyList<ProductCategory> Items, int TotalCount)> GetBrowsablePageAsync(
        Guid businessId,
        Guid? integrationConnectionId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ProductCategory> CreateAsync(ProductCategory category, CancellationToken ct = default);
    Task<ProductCategory> UpdateAsync(ProductCategory category, CancellationToken ct = default);
}
