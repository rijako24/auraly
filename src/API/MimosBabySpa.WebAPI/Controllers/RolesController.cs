using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    [PermissionAuthorize("roles.read")]
    public async Task<ActionResult<PagedResponse<RoleDto>>> GetAll(
        [FromQuery] PagedRequest request, CancellationToken ct)
    {
        return Ok(await _roleService.GetPagedByTenantAsync(User.GetTenantId(), request, ct));
    }

    [HttpGet("{roleId:guid}")]
    [PermissionAuthorize("roles.read")]
    public async Task<ActionResult<RoleDto>> GetById(Guid roleId, CancellationToken ct)
    {
        return Ok(await _roleService.GetByIdAsync(roleId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("roles.create")]
    public async Task<ActionResult<RoleDto>> Create(
        [FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var result = await _roleService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { roleId = result.RoleId }, result);
    }

    [HttpPut("{roleId:guid}")]
    [PermissionAuthorize("roles.update")]
    public async Task<ActionResult<RoleDto>> Update(
        Guid roleId, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        return Ok(await _roleService.UpdateAsync(roleId, request, ct));
    }

    [HttpDelete("{roleId:guid}")]
    [PermissionAuthorize("roles.delete")]
    public async Task<IActionResult> Deactivate(Guid roleId, CancellationToken ct)
    {
        await _roleService.DeactivateAsync(roleId, ct);
        return NoContent();
    }

    [HttpPost("{roleId:guid}/permissions")]
    [PermissionAuthorize("roles.assign_permissions")]
    public async Task<IActionResult> AssignPermissions(
        Guid roleId, [FromBody] AssignPermissionsRequest request, CancellationToken ct)
    {
        await _roleService.AssignPermissionsAsync(roleId, request, ct);
        return NoContent();
    }

    [HttpGet("{roleId:guid}/permissions")]
    [PermissionAuthorize("roles.read")]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetPermissions(
        Guid roleId, CancellationToken ct)
    {
        return Ok(await _roleService.GetRolePermissionsAsync(roleId, ct));
    }
}
