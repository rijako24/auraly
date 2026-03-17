using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Application.Identity;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class PermissionService : IPermissionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(IUnitOfWork unitOfWork, ILogger<PermissionService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllAsync(CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetAllAsync(ct);
        return permissions.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<PermissionDto>> GetByModuleAsync(string module, CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetByModuleAsync(module, ct);
        return permissions.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PermissionDto>>> GetGroupedByModuleAsync(CancellationToken ct)
    {
        var permissions = await _unitOfWork.Permissions.GetAllAsync(ct);
        return permissions
            .GroupBy(p => p.Module)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PermissionDto>)g.Select(MapToDto).ToList());
    }

    public async Task SeedPermissionsAsync(CancellationToken ct)
    {
        foreach (var (module, action, resource, description) in PermissionCatalog.All)
        {
            if (!await _unitOfWork.Permissions.ExistsByResourceAsync(resource, ct))
            {
                await _unitOfWork.Permissions.AddAsync(new Domain.Entities.Permission
                {
                    PermissionId = Guid.NewGuid(),
                    Module = module,
                    Action = action,
                    Resource = resource,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                }, ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Permission seed completed. Total: {Count}", PermissionCatalog.All.Length);
    }

    private static PermissionDto MapToDto(Domain.Entities.Permission p) => new(
        p.PermissionId, p.Module, p.Action, p.Resource, p.Description);
}
