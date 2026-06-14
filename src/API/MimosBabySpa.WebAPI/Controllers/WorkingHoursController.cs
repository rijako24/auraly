using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Authorize]
public class WorkingHoursController : ControllerBase
{
    private readonly IWorkingHoursAdminService _service;

    public WorkingHoursController(IWorkingHoursAdminService service)
    {
        _service = service;
    }

    [HttpGet("api/businesses/{businessId:guid}/working-hours")]
    [PermissionAuthorize("business_config.read")]
    public async Task<ActionResult<IReadOnlyList<WorkingHourDto>>> GetBusinessWorkingHours(
        Guid businessId,
        CancellationToken ct)
    {
        return Ok(await _service.GetBusinessWorkingHoursAsync(User.GetTenantId(), businessId, ct));
    }

    [HttpPut("api/businesses/{businessId:guid}/working-hours")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IReadOnlyList<WorkingHourDto>>> UpdateBusinessWorkingHours(
        Guid businessId,
        [FromBody] UpdateWorkingHoursRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateBusinessWorkingHoursAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpGet("api/employees/{employeeId:guid}/working-hours")]
    [PermissionAuthorize("employees.read")]
    public async Task<ActionResult<EmployeeWorkingHoursDto>> GetEmployeeWorkingHours(
        Guid employeeId,
        CancellationToken ct)
    {
        return Ok(await _service.GetEmployeeWorkingHoursAsync(User.GetTenantId(), employeeId, ct));
    }

    [HttpPut("api/employees/{employeeId:guid}/working-hours")]
    [PermissionAuthorize("employees.update")]
    public async Task<ActionResult<EmployeeWorkingHoursDto>> UpdateEmployeeWorkingHours(
        Guid employeeId,
        [FromBody] UpdateWorkingHoursRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateEmployeeWorkingHoursAsync(User.GetTenantId(), employeeId, request, ct));
    }
}
