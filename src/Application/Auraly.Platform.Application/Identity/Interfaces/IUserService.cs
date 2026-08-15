using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IUserService
{
    Task<UserDto> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<PagedResponse<UserDto>> GetPagedAsync(Guid tenantId, PagedRequest request, CancellationToken ct = default);
    Task<UserDto> CreateAsync(Guid tenantId, CreateUserRequest request, Guid createdByUserId, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(Guid userId, ResetUserPasswordRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid userId, CancellationToken ct = default);
    Task ActivateAsync(Guid userId, CancellationToken ct = default);
    Task AssignRoleAsync(Guid userId, AssignRoleRequest request, Guid assignedByUserId, CancellationToken ct = default);
    Task RemoveRoleAsync(Guid userId, Guid roleId, Guid? businessId, Guid actorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, Guid? businessId = null, CancellationToken ct = default);
}
