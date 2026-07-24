using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IProductCategoryRepository
{
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
