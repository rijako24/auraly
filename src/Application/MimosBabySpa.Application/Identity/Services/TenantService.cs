using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using Auraly.Contracts.Tenants;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public sealed class TenantService(
    IUnitOfWork unitOfWork,
    ITenantProvisioningStore provisioning,
    ILogger<TenantService> logger) : ITenantService
{
    public async Task<TenantDto> GetByIdAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        return MapToDto(tenant);
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

    public async Task<TenantDto> UpdateAsync(Guid tenantId, string? name, string? email, int? maximumUsers, int? maximumEnrolledDevices, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        if (name is not null) tenant.Name = name.Trim();
        if (email is not null) tenant.Email = email.Trim();
        tenant.UpdatedAt = DateTime.UtcNow;
        if (maximumUsers is < 1) throw new ArgumentException("El l\u00edmite de usuarios debe ser al menos 1.");
        if (maximumEnrolledDevices is < 0) throw new ArgumentException("El l\u00edmite de cajas no puede ser negativo.");
        if (maximumUsers.HasValue) tenant.MaximumUsers = maximumUsers.Value;
        if (maximumEnrolledDevices.HasValue) tenant.MaximumEnrolledDevices = maximumEnrolledDevices.Value;
        unitOfWork.Tenants.Update(tenant);
        await unitOfWork.SaveChangesAsync(ct);
        return MapToDto(tenant);
    }

    public async Task DeactivateAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        tenant.IsActive = false;
        tenant.UpdatedAt = DateTime.UtcNow;
        unitOfWork.Tenants.Update(tenant);
        await unitOfWork.SaveChangesAsync(ct);
        await unitOfWork.Tenants.RevokeActiveAuthenticationSessionsAsync(tenantId, DateTimeOffset.UtcNow, ct);
    }

    public async Task ActivateAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        tenant.IsActive = true;
        tenant.UpdatedAt = DateTime.UtcNow;
        unitOfWork.Tenants.Update(tenant);
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
        tenant.TenantId, tenant.Name, tenant.Email, tenant.IsActive, tenant.CreatedAt,
        tenant.Businesses?.Count ?? 0, tenant.MaximumUsers, tenant.MaximumEnrolledDevices,
        tenant.ActiveUserCount, tenant.ActiveEnrolledDeviceCount);
}
