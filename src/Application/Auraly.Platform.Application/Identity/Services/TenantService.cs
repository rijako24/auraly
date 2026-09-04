using Microsoft.Extensions.Logging;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Contracts.Tenants;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class TenantService(
    IUnitOfWork unitOfWork,
    ITenantProvisioningStore provisioning,
    IBlobStorageService blobStorage,
    IMediaUrlResolver mediaUrlResolver,
    ILogger<TenantService> logger,
    IPosPricingSynchronizationWriter pricingSynchronization,
    IPosSynchronizationOutboxDispatcher synchronization) : ITenantService
{
    public async Task<TenantDto> GetByIdAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        var expiration = (await unitOfWork.Tenants.GetFiscalCertificateExpirationsAsync(null, ct))
            .FirstOrDefault(value => value.TenantId == tenantId)?.ValidTo;
        return await MapToDtoWithBrandingAsync(tenant, expiration, ct);
    }

    public async Task<TenantBrandingDto> GetBrandingAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        return new(tenant.TenantId, tenant.Name, tenant.LegalName,
            await ResolveLogoUrlAsync(tenant, ct));
    }

    public async Task<PagedResponse<TenantDto>> GetPagedAsync(PagedRequest request, CancellationToken ct)
    {
        var (items, totalCount) = await unitOfWork.Tenants.GetPagedAsync(request.Page, request.PageSize, request.Search, ct);
        var expirations = (await unitOfWork.Tenants.GetFiscalCertificateExpirationsAsync(null, ct))
            .ToDictionary(value => value.TenantId, value => value.ValidTo);
        return new(items.Select(tenant => MapToDto(tenant,
            expirations.GetValueOrDefault(tenant.TenantId))).ToList(),
            totalCount, request.Page, request.PageSize);
    }

    public async Task<IReadOnlyList<FiscalCertificateExpiryAlertDto>> GetFiscalCertificateExpiryAlertsAsync(
        Guid actorTenantId, CancellationToken ct = default)
    {
        var actorTenant = await unitOfWork.Tenants.GetByIdAsync(actorTenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), actorTenantId);
        if (!string.Equals(actorTenant.TenantKey, PlatformPermissions.PlatformTenantKey,
                StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Las alertas de certificados DIAN pertenecen a la administración de plataforma Auraly.");
        var now = DateTimeOffset.UtcNow;
        var expirations = await unitOfWork.Tenants.GetFiscalCertificateExpirationsAsync(
            now.AddDays(30), ct);
        return expirations.Select(value => new FiscalCertificateExpiryAlertDto(
            value.TenantId, value.TenantName, value.ValidTo, value.ValidTo <= now)).ToList();
    }

    public async Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, Guid? actorUserId,
        TenantQuoteDto commercialQuote, CancellationToken ct)
    {
        TenantProvisioningRequestValidator.Validate(request);
        if (!await unitOfWork.Tenants.IsReferenceOptionActiveAsync("tenant-entity-type", request.EntityType, ct)
            || !await unitOfWork.Tenants.IsReferenceOptionActiveAsync("tenant-identification-type", request.IdentificationTypeCode, ct))
            throw new ArgumentException("Selecciona un tipo de persona y de identificación vigentes.");
        ArgumentNullException.ThrowIfNull(commercialQuote);
        if (request.MaximumUsers != checked(commercialQuote.FullUserLimit + commercialQuote.SellerUserLimit)
            || request.MaximumEnrolledDevices != commercialQuote.PosDeviceLimit)
            throw new ArgumentException("Los cupos del tenant no coinciden con la cotización aprobada.");
        var result = await provisioning.ProvisionAsync(request, actorUserId, commercialQuote, ct);
        logger.LogInformation("Tenant {TenantId} provisioned with business {BusinessId}", result.TenantId, result.BusinessId);
        return result;
    }

    public async Task<TenantDto> UpdateAsync(Guid tenantId, string? name, string? email,
        int? maximumUsers, int? maximumEnrolledDevices, string? legalName = null,
        string? nit = null, string? verificationDigit = null, string? entityType = null,
        string? identificationTypeCode = null, string? inventoryCostBasis = null,
        bool? allowPromotionChannelCombination = null,
        CancellationToken ct = default)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El nombre comercial es obligatorio.");
            tenant.Name = name.Trim();
        }
        if (email is not null)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                throw new ArgumentException("El correo empresarial no es válido.");
            tenant.Email = email.Trim();
        }
        tenant.UpdatedAt = DateTime.UtcNow;
        if (maximumUsers is < 1) throw new ArgumentException("El l\u00edmite de usuarios debe ser al menos 1.");
        if (maximumEnrolledDevices is < 0) throw new ArgumentException("El l\u00edmite de cajas no puede ser negativo.");
        if (maximumUsers < tenant.ActiveUserCount)
            throw new ConflictException($"El cupo no puede ser menor que los {tenant.ActiveUserCount} usuarios activos actuales.");
        if (maximumEnrolledDevices < tenant.ActiveEnrolledDeviceCount)
            throw new ConflictException($"El cupo no puede ser menor que las {tenant.ActiveEnrolledDeviceCount} cajas enroladas actuales.");
        if (maximumUsers.HasValue) tenant.MaximumUsers = maximumUsers.Value;
        if (maximumEnrolledDevices.HasValue) tenant.MaximumEnrolledDevices = maximumEnrolledDevices.Value;
        if (inventoryCostBasis is not null)
        {
            if (inventoryCostBasis is not ("LatestReceiptCost" or "WeightedAverageCost"))
                throw new ArgumentException("La base de costo de inventario no es válida.");
            tenant.InventoryCostBasis = inventoryCostBasis;
        }
        var promotionPolicyChanged = allowPromotionChannelCombination.HasValue
            && tenant.AllowPromotionChannelCombination != allowPromotionChannelCombination.Value;
        if (allowPromotionChannelCombination.HasValue)
            tenant.AllowPromotionChannelCombination = allowPromotionChannelCombination.Value;
        var changesLegalIdentity = legalName is not null || nit is not null || verificationDigit is not null
            || entityType is not null || identificationTypeCode is not null;
        if (changesLegalIdentity)
        {
            var nextLegalName = legalName?.Trim() ?? tenant.LegalName;
            var nextNit = nit?.Trim() ?? tenant.Nit;
            var nextEntityType = entityType?.Trim() ?? tenant.EntityType ?? "Organization";
            var nextIdentificationType = identificationTypeCode?.Trim() ?? tenant.IdentificationTypeCode ?? "NIT";
            if (!await unitOfWork.Tenants.IsReferenceOptionActiveAsync("tenant-entity-type", nextEntityType, ct)
                || !await unitOfWork.Tenants.IsReferenceOptionActiveAsync("tenant-identification-type", nextIdentificationType, ct))
                throw new ArgumentException("Selecciona un tipo de persona y de identificación vigentes.");
            if (nextEntityType == "NaturalPerson" && nextIdentificationType == "NIT"
                || nextEntityType == "Organization" && nextIdentificationType != "NIT")
                throw new ArgumentException("La persona natural usa un documento personal y la persona jurídica usa NIT.");
            var nextVerificationDigit = nextIdentificationType == "NIT"
                ? verificationDigit?.Trim() ?? tenant.VerificationDigit
                : null;
            if (string.IsNullOrWhiteSpace(nextLegalName) || string.IsNullOrWhiteSpace(nextNit)
                || nextIdentificationType == "NIT" && string.IsNullOrWhiteSpace(nextVerificationDigit))
                throw new ArgumentException("Completa la identidad legal y el documento del tenant.");
            TenantProvisioningRequestValidator.ValidateIdentification(
                nextIdentificationType, nextNit!, nextVerificationDigit);
            if (!await unitOfWork.Tenants.UpdateLegalIdentityAsync(tenantId, nextLegalName, nextNit,
                    nextVerificationDigit, nextEntityType, nextIdentificationType, DateTimeOffset.UtcNow, ct))
                throw new ConflictException("El tenant no tiene un perfil legal editable.");
            tenant.LegalName = nextLegalName;
            tenant.Nit = nextNit;
            tenant.VerificationDigit = nextVerificationDigit;
            tenant.EntityType = nextEntityType;
            tenant.IdentificationTypeCode = nextIdentificationType;
        }
        await unitOfWork.SaveChangesAsync(ct);
        if (promotionPolicyChanged)
        {
            var businessIds = (await unitOfWork.Businesses.GetByTenantIdAsync(tenantId, ct))
                .Where(business => business.IsActive)
                .Select(business => business.BusinessId)
                .ToArray();
            await pricingSynchronization.EnqueueBusinessesAsync(businessIds, ct);
            foreach (var businessId in businessIds)
                await synchronization.DispatchPendingAsync(
                    tenantId, businessId, CancellationToken.None);
        }
        var expiration = (await unitOfWork.Tenants.GetFiscalCertificateExpirationsAsync(null, ct))
            .FirstOrDefault(value => value.TenantId == tenantId)?.ValidTo;
        return await MapToDtoWithBrandingAsync(tenant, expiration, ct);
    }

    public async Task<TenantDto> UploadLogoAsync(Guid tenantId, Stream stream, string fileName,
        CancellationToken ct = default)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        if (tenant.PrimaryBusinessId is not { } businessId)
            throw new ConflictException("El tenant no tiene una sede principal para almacenar su logo.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new ArgumentException("Usa un logo JPG, PNG o WEBP.");
        var mediaRef = await blobStorage.UploadImageAsync(
            businessId, stream, $"tenant-branding/{Guid.NewGuid():N}{extension}");
        if (!await unitOfWork.Tenants.UpdateLogoAsync(tenantId, mediaRef, DateTimeOffset.UtcNow, ct))
            throw new ConflictException("El tenant no tiene un perfil legal editable.");
        tenant.LogoMediaRef = mediaRef;
        var expiration = (await unitOfWork.Tenants.GetFiscalCertificateExpirationsAsync(null, ct))
            .FirstOrDefault(value => value.TenantId == tenantId)?.ValidTo;
        return await MapToDtoWithBrandingAsync(tenant, expiration, ct);
    }

    public async Task DeactivateAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        tenant.IsActive = false;
        tenant.UpdatedAt = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);
        await unitOfWork.Tenants.RevokeActiveAuthenticationSessionsAsync(tenantId, DateTimeOffset.UtcNow, ct);
    }

    public async Task ActivateAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        tenant.IsActive = true;
        tenant.UpdatedAt = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);

    }
    private static TenantDto MapToDto(Tenant tenant, DateTimeOffset? fiscalCertificateValidTo = null) => new(
        tenant.TenantId, tenant.TenantKey, tenant.Name, tenant.Email, tenant.IsActive,
        tenant.CreatedAt, tenant.Businesses?.Count ?? 0,
        tenant.MaximumUsers, tenant.MaximumEnrolledDevices, tenant.InventoryCostBasis,
        tenant.AllowPromotionChannelCombination,
        tenant.ActiveUserCount, tenant.ActiveEnrolledDeviceCount,
        tenant.LegalName, tenant.Nit, tenant.VerificationDigit,
        tenant.EntityType, tenant.IdentificationTypeCode, null, fiscalCertificateValidTo);

    private async Task<TenantDto> MapToDtoWithBrandingAsync(
        Tenant tenant, DateTimeOffset? fiscalCertificateValidTo, CancellationToken ct)
    {
        var value = MapToDto(tenant, fiscalCertificateValidTo);
        return value with { LogoUrl = await ResolveLogoUrlAsync(tenant, ct) };
    }

    private async Task<string?> ResolveLogoUrlAsync(Tenant tenant, CancellationToken ct)
    {
        if (tenant.PrimaryBusinessId is not { } businessId
            || string.IsNullOrWhiteSpace(tenant.LogoMediaRef))
            return null;
        return await mediaUrlResolver.ResolveAsync(businessId, tenant.LogoMediaRef, ct);
    }
}
