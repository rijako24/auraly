using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Authorize]
public class WorkingHoursController : ControllerBase
{
    private readonly IWorkingHoursAdminService _service;

    public WorkingHoursController(IWorkingHoursAdminService service)
    {
        _service = service;
    }

    [HttpGet("api/v1/businesses/{businessId:guid}/working-hours")]
    [PermissionAuthorize("business_config.read")]
    public async Task<ActionResult<IReadOnlyList<WorkingHourDto>>> GetBusinessWorkingHours(Guid businessId, CancellationToken ct)
    {
        return Ok(await _service.GetBusinessWorkingHoursAsync(User.GetTenantId(), businessId, ct));
    }

    [HttpPut("api/v1/businesses/{businessId:guid}/working-hours")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IReadOnlyList<WorkingHourDto>>> UpdateBusinessWorkingHours(Guid businessId, [FromBody] UpdateWorkingHoursRequest request, CancellationToken ct)
    {
        return Ok(await _service.UpdateBusinessWorkingHoursAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpGet("api/v1/businesses/{businessId:guid}/availability-blocks")]
    [PermissionAuthorize("business_config.read")]
    public async Task<ActionResult<IReadOnlyList<BusinessAvailabilityBlockDto>>> GetBusinessAvailabilityBlocks(
        Guid businessId,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken ct)
    {
        return Ok(await _service.GetBusinessAvailabilityBlocksAsync(User.GetTenantId(), businessId, startDate, endDate, ct));
    }

    [HttpPost("api/v1/businesses/{businessId:guid}/availability-blocks")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<BusinessAvailabilityBlockDto>> CreateBusinessAvailabilityBlock(
        Guid businessId,
        [FromBody] UpsertBusinessAvailabilityBlockRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreateBusinessAvailabilityBlockAsync(User.GetTenantId(), businessId, request, ct);
        return CreatedAtAction(nameof(GetBusinessAvailabilityBlocks), new { businessId }, result);
    }

    [HttpPut("api/v1/businesses/{businessId:guid}/availability-blocks/{blockId:guid}")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<BusinessAvailabilityBlockDto>> UpdateBusinessAvailabilityBlock(
        Guid businessId,
        Guid blockId,
        [FromBody] UpsertBusinessAvailabilityBlockRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateBusinessAvailabilityBlockAsync(User.GetTenantId(), businessId, blockId, request, ct));
    }

    [HttpDelete("api/v1/businesses/{businessId:guid}/availability-blocks/{blockId:guid}")]
    [PermissionAuthorize("business_config.update")]
    public async Task<IActionResult> DeleteBusinessAvailabilityBlock(Guid businessId, Guid blockId, CancellationToken ct)
    {
        await _service.DeleteBusinessAvailabilityBlockAsync(User.GetTenantId(), businessId, blockId, ct);
        return NoContent();
    }

    [HttpGet("api/v1/employees/{employeeId:guid}/working-hours")]
    [PermissionAuthorize("employees.read")]
    public async Task<ActionResult<EmployeeWorkingHoursDto>> GetEmployeeWorkingHours(Guid employeeId, CancellationToken ct)
    {
        return Ok(await _service.GetEmployeeWorkingHoursAsync(User.GetTenantId(), employeeId, ct));
    }

    [HttpPut("api/v1/employees/{employeeId:guid}/working-hours")]
    [PermissionAuthorize("employees.update")]
    public async Task<ActionResult<EmployeeWorkingHoursDto>> UpdateEmployeeWorkingHours(Guid employeeId, [FromBody] UpdateWorkingHoursRequest request, CancellationToken ct)
    {
        return Ok(await _service.UpdateEmployeeWorkingHoursAsync(User.GetTenantId(), employeeId, request, ct));
    }

    [HttpGet("api/v1/employees/{employeeId:guid}/schedule-exceptions")]
    [PermissionAuthorize("employees.read")]
    public async Task<ActionResult<IReadOnlyList<EmployeeScheduleExceptionDto>>> GetEmployeeScheduleExceptions(Guid employeeId, CancellationToken ct)
    {
        return Ok(await _service.GetEmployeeScheduleExceptionsAsync(User.GetTenantId(), employeeId, ct));
    }

    [HttpPost("api/v1/employees/{employeeId:guid}/schedule-exceptions")]
    [PermissionAuthorize("employees.update")]
    public async Task<ActionResult<EmployeeScheduleExceptionDto>> CreateEmployeeScheduleException(
        Guid employeeId,
        [FromBody] UpsertEmployeeScheduleExceptionRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreateEmployeeScheduleExceptionAsync(User.GetTenantId(), employeeId, request, ct);
        return CreatedAtAction(nameof(GetEmployeeScheduleExceptions), new { employeeId }, result);
    }

    [HttpPut("api/v1/employees/{employeeId:guid}/schedule-exceptions/{exceptionId:guid}")]
    [PermissionAuthorize("employees.update")]
    public async Task<ActionResult<EmployeeScheduleExceptionDto>> UpdateEmployeeScheduleException(
        Guid employeeId,
        Guid exceptionId,
        [FromBody] UpsertEmployeeScheduleExceptionRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateEmployeeScheduleExceptionAsync(User.GetTenantId(), employeeId, exceptionId, request, ct));
    }

    [HttpDelete("api/v1/employees/{employeeId:guid}/schedule-exceptions/{exceptionId:guid}")]
    [PermissionAuthorize("employees.update")]
    public async Task<IActionResult> DeleteEmployeeScheduleException(Guid employeeId, Guid exceptionId, CancellationToken ct)
    {
        await _service.DeleteEmployeeScheduleExceptionAsync(User.GetTenantId(), employeeId, exceptionId, ct);
        return NoContent();
    }
}
