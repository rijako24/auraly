using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeAdminService _service;

    public EmployeesController(IEmployeeAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("employees.read")]
    public async Task<ActionResult<PagedResponse<EmployeeDto>>> GetByBusiness(
        [FromQuery] Guid businessId, [FromQuery] PagedRequest request, CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpGet("{employeeId:guid}")]
    [PermissionAuthorize("employees.read")]
    public async Task<ActionResult<EmployeeDto>> GetById(Guid employeeId, CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), employeeId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("employees.create")]
    public async Task<ActionResult<EmployeeDto>> Create(
        [FromBody] CreateEmployeeRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(User.GetTenantId(), request, ct);
        return CreatedAtAction(nameof(GetById), new { employeeId = result.EmployeeId }, result);
    }

    [HttpPut("{employeeId:guid}")]
    [PermissionAuthorize("employees.update")]
    public async Task<ActionResult<EmployeeDto>> Update(
        Guid employeeId, [FromBody] UpdateEmployeeRequest request, CancellationToken ct)
    {
        return Ok(await _service.UpdateAsync(User.GetTenantId(), employeeId, request, ct));
    }

    [HttpDelete("{employeeId:guid}")]
    [PermissionAuthorize("employees.delete")]
    public async Task<IActionResult> Deactivate(Guid employeeId, CancellationToken ct)
    {
        await _service.DeactivateAsync(User.GetTenantId(), employeeId, ct);
        return NoContent();
    }
}
