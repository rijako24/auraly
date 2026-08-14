using Auraly.Platform.Application.Commerce;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IProductAliasAdminService
{
    Task<IReadOnlyList<ProductAliasDto>> GetByProductAsync(Guid tenantId, Guid businessId, Guid productId, CancellationToken ct = default);
    Task<ProductAliasImportResult> ImportAsync(Guid tenantId, Guid businessId, ProductAliasImportRequest request, CancellationToken ct = default);
    Task<ProductAliasDto> ReviewAsync(Guid tenantId, Guid businessId, Guid productId, Guid productAliasId, ReviewProductAliasRequest request, CancellationToken ct = default);
    Task<ProductAliasDto> PromoteAsync(Guid tenantId, Guid businessId, Guid productId, Guid productAliasId, PromoteProductAliasRequest request, CancellationToken ct = default);
}
