using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IPermissionService
{
    Task<IReadOnlyList<PermissionDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PermissionDto>> GetByModuleAsync(string module, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, IReadOnlyList<PermissionDto>>> GetGroupedByModuleAsync(CancellationToken ct = default);
    Task SeedPermissionsAsync(CancellationToken ct = default);
}
