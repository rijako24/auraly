using Auraly.Platform.Application.Common.DTOs;
using Auraly.Contracts.Tenants;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface ITenantService
{
    Task<TenantDto> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantBrandingDto> GetBrandingAsync(Guid tenantId, CancellationToken ct = default);
    Task<PagedResponse<TenantDto>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<FiscalCertificateExpiryAlertDto>> GetFiscalCertificateExpiryAlertsAsync(
        Guid actorTenantId, CancellationToken ct = default);
    Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, Guid? actorUserId,
        TenantQuoteDto commercialQuote, CancellationToken ct = default);
    Task<TenantDto> UpdateAsync(Guid tenantId, string? name, string? email, int? maximumUsers,
        int? maximumEnrolledDevices, string? legalName = null, string? nit = null,
        string? verificationDigit = null, string? entityType = null,
        string? identificationTypeCode = null, string? inventoryCostBasis = null,
        CancellationToken ct = default);
    Task<TenantDto> UploadLogoAsync(Guid tenantId, Stream stream, string fileName, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, CancellationToken ct = default);
    Task ActivateAsync(Guid tenantId, CancellationToken ct = default);
}
