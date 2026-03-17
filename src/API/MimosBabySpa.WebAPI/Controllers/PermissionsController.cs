using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/permissions")]
[Authorize]
public class PermissionsController : ControllerBase
{
    private readonly IPermissionService _permissionService;

    public PermissionsController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet]
    [PermissionAuthorize("permissions.read")]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetAll(CancellationToken ct)
    {
        return Ok(await _permissionService.GetAllAsync(ct));
    }

    [HttpGet("by-module/{module}")]
    [PermissionAuthorize("permissions.read")]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetByModule(
        string module, CancellationToken ct)
    {
        return Ok(await _permissionService.GetByModuleAsync(module, ct));
    }

    [HttpGet("grouped")]
    [PermissionAuthorize("permissions.read")]
    public async Task<ActionResult<IReadOnlyDictionary<string, IReadOnlyList<PermissionDto>>>> GetGrouped(
        CancellationToken ct)
    {
        return Ok(await _permissionService.GetGroupedByModuleAsync(ct));
    }
}
