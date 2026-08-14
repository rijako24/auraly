using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class ProductAliasRepository : IProductAliasRepository
{
    private readonly ApplicationDbContext _context;

    public ProductAliasRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<ProductAlias>> FindActiveAsync(
        Guid businessId, string normalizedAlias, string customerKey, CancellationToken ct = default) =>
        await _context.ProductAliases
            .AsNoTracking()
            .Include(alias => alias.Product)
            .Where(alias => alias.BusinessId == businessId
                && alias.NormalizedAlias == normalizedAlias
                && alias.Status == ProductAliasStatus.Active
                && (alias.Scope == ProductAliasScope.Business
                    || alias.Scope == ProductAliasScope.Customer && alias.CustomerKey == customerKey))
            .OrderByDescending(alias => alias.Scope)
            .ThenByDescending(alias => alias.ResolutionMode)
            .ThenByDescending(alias => alias.UsageCount)
            .ToListAsync(ct);

    public Task<ProductAlias?> GetByIdAsync(
        Guid businessId, Guid productId, Guid productAliasId, CancellationToken ct = default) =>
        _context.ProductAliases.FirstOrDefaultAsync(alias =>
            alias.BusinessId == businessId
            && alias.ProductId == productId
            && alias.ProductAliasId == productAliasId,
            ct);

    public async Task<IReadOnlyList<ProductAlias>> GetByProductAsync(Guid businessId, Guid productId, CancellationToken ct = default) =>
        await _context.ProductAliases.AsNoTracking()
            .Where(alias => alias.BusinessId == businessId && alias.ProductId == productId)
            .OrderBy(alias => alias.Alias)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductAlias>> GetByBusinessAsync(Guid businessId, CancellationToken ct = default) =>
        await _context.ProductAliases
            .Where(alias => alias.BusinessId == businessId).ToListAsync(ct);

    public Task<ProductAlias?> GetMappingAsync(
        Guid businessId, Guid productId, ProductAliasScope scope, string customerKey, string normalizedAlias, CancellationToken ct = default) =>
        _context.ProductAliases.FirstOrDefaultAsync(alias =>
            alias.BusinessId == businessId && alias.ProductId == productId && alias.Scope == scope
            && alias.CustomerKey == customerKey && alias.NormalizedAlias == normalizedAlias, ct);

    public async Task<IReadOnlyList<ProductAlias>> FindConflictsAsync(
        Guid businessId, ProductAliasScope scope, string customerKey, string normalizedAlias, Guid exceptProductId, CancellationToken ct = default) =>
        await _context.ProductAliases.AsNoTracking()
            .Where(alias => alias.BusinessId == businessId && alias.Scope == scope
                && alias.CustomerKey == customerKey && alias.NormalizedAlias == normalizedAlias
                && alias.ProductId != exceptProductId && alias.Status != ProductAliasStatus.Rejected)
            .ToListAsync(ct);

    public Task<ProductAlias> CreateAsync(ProductAlias alias, CancellationToken ct = default)
    {
        _context.ProductAliases.Add(alias);
        return Task.FromResult(alias);
    }

    public Task<ProductAlias> UpdateAsync(ProductAlias alias, CancellationToken ct = default)
    {
        _context.ProductAliases.Update(alias);
        return Task.FromResult(alias);
    }
}
