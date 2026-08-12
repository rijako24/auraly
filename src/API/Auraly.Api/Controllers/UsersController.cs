using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [PermissionAuthorize("users.read")]
    public async Task<ActionResult<PagedResponse<UserDto>>> GetAll(
        [FromQuery] PagedRequest request, CancellationToken ct)
    {
        return Ok(await _userService.GetPagedAsync(User.GetTenantId(), request, ct));
    }

    [HttpGet("{userId:guid}")]
    [PermissionAuthorize("users.read")]
    public async Task<ActionResult<UserDto>> GetById(Guid userId, CancellationToken ct)
    {
        return Ok(await _userService.GetByIdAsync(userId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("users.create")]
    public async Task<ActionResult<UserDto>> Create(
        [FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var result = await _userService.CreateAsync(User.GetTenantId(), request, User.GetUserId(), ct);
        return CreatedAtAction(nameof(GetById), new { userId = result.UserId }, result);
    }

    [HttpPut("{userId:guid}")]
    [PermissionAuthorize("users.update")]
    public async Task<ActionResult<UserDto>> Update(
        Guid userId, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        return Ok(await _userService.UpdateAsync(userId, request, ct));
    }

    [HttpPost("{userId:guid}/deactivate")]
    [PermissionAuthorize("users.delete")]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken ct)
    {
        await _userService.DeactivateAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("{userId:guid}/activate")]
    [PermissionAuthorize("users.delete")]
    public async Task<IActionResult> Activate(Guid userId, CancellationToken ct)
    {
        await _userService.ActivateAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("{userId:guid}/roles")]
    [PermissionAuthorize("users.assign_role")]
    public async Task<IActionResult> AssignRole(
        Guid userId, [FromBody] AssignRoleRequest request, CancellationToken ct)
    {
        await _userService.AssignRoleAsync(userId, request, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/roles/{roleId:guid}")]
    [PermissionAuthorize("users.remove_role")]
    public async Task<IActionResult> RemoveRole(
        Guid userId, Guid roleId, [FromQuery] Guid? businessId, CancellationToken ct)
    {
        await _userService.RemoveRoleAsync(userId, roleId, businessId, ct);
        return NoContent();
    }

    [HttpGet("{userId:guid}/permissions")]
    [PermissionAuthorize("users.read")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetPermissions(
        Guid userId, [FromQuery] Guid? businessId, CancellationToken ct)
    {
        return Ok(await _userService.GetUserPermissionsAsync(userId, businessId, ct));
    }
}
