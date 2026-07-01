using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> SearchAsync(
        Guid businessId,
        string? query,
        string? category,
        int limit,
        CancellationToken ct = default,
        bool includeInactive = false);

    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search = null,
        bool includeInactive = false,
        CancellationToken ct = default);

    Task<Product?> GetByIdAsync(Guid businessId, Guid productId, CancellationToken ct = default);
    Task<Product?> GetByExternalIdAsync(Guid businessId, Guid integrationConnectionId, string externalProductId, CancellationToken ct = default);
    Task<Product> CreateAsync(Product product, CancellationToken ct = default);
    Task<Product> UpdateAsync(Product product, CancellationToken ct = default);
}
