using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/roles")]
[Authorize]
public class RolesController(IRoleService roleService) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize("roles.read")]
    public async Task<ActionResult<PagedResponse<RoleDto>>> GetAll([FromQuery] PagedRequest request, CancellationToken ct) =>
        Ok(await roleService.GetPagedByTenantAsync(User.GetTenantId(), request, ct));

    [HttpGet("{roleId:guid}")]
    [PermissionAuthorize("roles.read")]
    public async Task<ActionResult<RoleDto>> GetById(Guid roleId, CancellationToken ct)
    {
        var role = await GetScopedRoleAsync(roleId, ct);
        return Ok(role);
    }

    [HttpPost]
    [PermissionAuthorize("roles.create")]
    public async Task<ActionResult<RoleDto>> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        if (request.TenantId.HasValue && request.TenantId.Value != User.GetTenantId())
            throw new ForbiddenException("No puede crear roles para otra organización.");
        var result = await roleService.CreateAsync(User.GetTenantId(), request, ct);
        return CreatedAtAction(nameof(GetById), new { roleId = result.RoleId }, result);
    }

    [HttpPut("{roleId:guid}")]
    [PermissionAuthorize("roles.update")]
    public async Task<ActionResult<RoleDto>> Update(Guid roleId, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        await GetScopedRoleAsync(roleId, ct);
        return Ok(await roleService.UpdateAsync(roleId, request, ct));
    }

    [HttpDelete("{roleId:guid}")]
    [PermissionAuthorize("roles.delete")]
    public async Task<IActionResult> Deactivate(Guid roleId, CancellationToken ct)
    {
        await GetScopedRoleAsync(roleId, ct);
        await roleService.DeactivateAsync(roleId, ct);
        return NoContent();
    }

    [HttpPost("{roleId:guid}/permissions")]
    [PermissionAuthorize("roles.assign_permissions")]
    public async Task<IActionResult> AssignPermissions(Guid roleId, [FromBody] AssignPermissionsRequest request, CancellationToken ct)
    {
        await GetScopedRoleAsync(roleId, ct);
        await roleService.AssignPermissionsAsync(roleId, request, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpGet("{roleId:guid}/permissions")]
    [PermissionAuthorize("roles.read")]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetPermissions(Guid roleId, CancellationToken ct)
    {
        await GetScopedRoleAsync(roleId, ct);
        return Ok(await roleService.GetRolePermissionsAsync(roleId, ct));
    }

    private async Task<RoleDto> GetScopedRoleAsync(Guid roleId, CancellationToken ct)
    {
        var role = await roleService.GetByIdAsync(roleId, ct);
        if (role.TenantId == User.GetTenantId()) return role;
        throw new ForbiddenException("No puede administrar roles de otra organización.");
    }
}