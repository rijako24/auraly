using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface ICatalogImportAdminService
{
    Task<CatalogImportDraftDto> ExtractFromDocumentAsync(
        Guid tenantId,
        Guid businessId,
        Stream fileStream,
        string fileName,
        CancellationToken ct = default);

    Task<CatalogImportResultDto> ConfirmImportAsync(
        Guid tenantId,
        Guid businessId,
        ConfirmCatalogImportRequest request,
        CancellationToken ct = default);
}
