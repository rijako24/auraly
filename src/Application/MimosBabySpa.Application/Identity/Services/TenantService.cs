using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class TenantService : ITenantService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TenantService> _logger;

    public TenantService(IUnitOfWork unitOfWork, ILogger<TenantService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TenantDto> GetByIdAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        return MapToDto(tenant);
    }

    public async Task<PagedResponse<TenantDto>> GetPagedAsync(PagedRequest request, CancellationToken ct)
    {
        var (items, totalCount) = await _unitOfWork.Tenants.GetPagedAsync(
            request.Page, request.PageSize, request.Search, ct);

        return new PagedResponse<TenantDto>(
            items.Select(MapToDto).ToList(),
            totalCount, request.Page, request.PageSize);
    }

    public async Task<TenantDto> CreateAsync(string name, string email, CancellationToken ct)
    {
        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            Name = name,
            Email = email,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Tenants.AddAsync(tenant, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant '{Name}' created", tenant.Name);

        return MapToDto(tenant);
    }

    public async Task<TenantDto> UpdateAsync(Guid tenantId, string? name, string? email, CancellationToken ct)
    {
        var tenant = await _unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        if (name is not null) tenant.Name = name;
        if (email is not null) tenant.Email = email;
        tenant.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Tenants.Update(tenant);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(tenant);
    }

    public async Task DeactivateAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _unitOfWork.Tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        tenant.IsActive = false;
        tenant.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Tenants.Update(tenant);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static TenantDto MapToDto(Tenant t) => new(
        t.TenantId, t.Name, t.Email, t.IsActive, t.CreatedAt,
        t.Businesses?.Count ?? 0);
}
