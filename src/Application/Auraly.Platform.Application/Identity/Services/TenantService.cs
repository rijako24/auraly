using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Contracts.Tenants;
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
    ILogger<TenantService> logger) : ITenantService
{
    public async Task<TenantDto> GetByIdAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        return await MapToDtoWithBrandingAsync(tenant, ct);
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
        return new(items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, Guid? actorUserId, CancellationToken ct)
    {
        Validate(request);
        var result = await provisioning.ProvisionAsync(request, actorUserId, ct);
        logger.LogInformation("Tenant {TenantId} provisioned with business {BusinessId}", result.TenantId, result.BusinessId);
        return result;
    }

    public async Task<TenantDto> UpdateAsync(Guid tenantId, string? name, string? email,
        int? maximumUsers, int? maximumEnrolledDevices, string? legalName = null,
        string? nit = null, string? verificationDigit = null, string? entityType = null,
        string? identificationTypeCode = null, CancellationToken ct = default)
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
            if (nextEntityType == "NaturalPerson" && nextIdentificationType != "CC"
                || nextEntityType == "Organization" && nextIdentificationType != "NIT")
                throw new ArgumentException("La persona natural se identifica con cédula y la persona jurídica con NIT.");
            var nextVerificationDigit = nextIdentificationType == "NIT"
                ? verificationDigit?.Trim() ?? tenant.VerificationDigit
                : null;
            if (string.IsNullOrWhiteSpace(nextLegalName) || string.IsNullOrWhiteSpace(nextNit)
                || nextIdentificationType == "NIT" && string.IsNullOrWhiteSpace(nextVerificationDigit))
                throw new ArgumentException("Completa la identidad legal y el documento del tenant.");
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
        return await MapToDtoWithBrandingAsync(tenant, ct);
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
        return await MapToDtoWithBrandingAsync(tenant, ct);
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
    private static void Validate(ProvisionTenantRequest request)
    {
        if (request.ProvisioningRequestId == Guid.Empty || request.CountryId == Guid.Empty || request.AdministrativeDivisionId == Guid.Empty || request.CityId == Guid.Empty)
            throw new ArgumentException("La solicitud, país, departamento y ciudad son obligatorios.");
        var required = new[] { request.LegalName, request.TradeName, request.Nit, request.VerificationDigit,
            request.Address, request.Phone, request.Email, request.BusinessName, request.BusinessAddress,
            request.BusinessPhone, request.BusinessEmail, request.InvitationEmail };
        if (required.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Completa todos los datos obligatorios de empresa, sede y administrador.");
        if (request.InventoryCostBasis is not ("LatestReceiptCost" or "WeightedAverageCost"))
            throw new ArgumentException("La base de costo de inventario no es válida.");
        if (!request.Email.Contains('@') || !request.BusinessEmail.Contains('@') || !request.InvitationEmail.Contains('@'))
            throw new ArgumentException("Los correos de empresa, sede e invitación no son válidos.");
        if (request.MaximumUsers < 1)
            throw new ArgumentException("El límite de usuarios debe ser al menos 1.");
        if (request.MaximumEnrolledDevices < 0)
            throw new ArgumentException("El límite de cajas no puede ser negativo.");
    }

    private static TenantDto MapToDto(Tenant tenant) => new(
        tenant.TenantId, tenant.TenantKey, tenant.Name, tenant.Email, tenant.IsActive,
        tenant.CreatedAt, tenant.Businesses?.Count ?? 0,
        tenant.MaximumUsers, tenant.MaximumEnrolledDevices,
        tenant.ActiveUserCount, tenant.ActiveEnrolledDeviceCount,
        tenant.LegalName, tenant.Nit, tenant.VerificationDigit,
        tenant.EntityType, tenant.IdentificationTypeCode, null);

    private async Task<TenantDto> MapToDtoWithBrandingAsync(Tenant tenant, CancellationToken ct)
    {
        var value = MapToDto(tenant);
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
