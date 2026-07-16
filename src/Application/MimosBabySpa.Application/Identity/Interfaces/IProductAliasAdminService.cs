using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IProductAliasAdminService
{
    Task<IReadOnlyList<ProductAliasDto>> GetByProductAsync(Guid tenantId, Guid businessId, Guid productId, CancellationToken ct = default);
    Task<ProductAliasImportResult> ImportAsync(Guid tenantId, Guid businessId, ProductAliasImportRequest request, CancellationToken ct = default);
}
