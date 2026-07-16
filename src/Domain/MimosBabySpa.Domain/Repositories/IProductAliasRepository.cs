using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IProductAliasRepository
{
    Task<IReadOnlyList<ProductAlias>> FindActiveAsync(Guid businessId, string normalizedAlias, string customerKey, CancellationToken ct = default);
    Task<IReadOnlyList<ProductAlias>> GetByProductAsync(Guid businessId, Guid productId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductAlias>> GetByBusinessAsync(Guid businessId, CancellationToken ct = default);
    Task<ProductAlias?> GetMappingAsync(Guid businessId, Guid productId, ProductAliasScope scope, string customerKey, string normalizedAlias, CancellationToken ct = default);
    Task<IReadOnlyList<ProductAlias>> FindConflictsAsync(Guid businessId, ProductAliasScope scope, string customerKey, string normalizedAlias, Guid exceptProductId, CancellationToken ct = default);
    Task<ProductAlias> CreateAsync(ProductAlias alias, CancellationToken ct = default);
    Task<ProductAlias> UpdateAsync(ProductAlias alias, CancellationToken ct = default);
}
