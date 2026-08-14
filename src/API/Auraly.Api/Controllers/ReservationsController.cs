using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/reservations")]
[Authorize]
public class ReservationsController : ControllerBase
{
    private readonly IReservationAdminService _service;

    public ReservationsController(IReservationAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("reservations.read")]
    public async Task<ActionResult<PagedResponse<ReservationDto>>> GetByBusiness(
        [FromQuery] Guid businessId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(
            User.GetTenantId(), businessId, request, startDate, endDate, ct));
    }

    [HttpGet("{reservationId:guid}")]
    [PermissionAuthorize("reservations.read")]
    public async Task<ActionResult<ReservationDto>> GetById(Guid reservationId, CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), reservationId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("reservations.create")]
    public async Task<ActionResult<ReservationDto>> Create(
        [FromBody] CreateReservationRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(User.GetTenantId(), request, ct);
        return CreatedAtAction(nameof(GetById), new { reservationId = result.ReservationId }, result);
    }

    [HttpPut("{reservationId:guid}")]
    [PermissionAuthorize("reservations.update")]
    public async Task<ActionResult<ReservationDto>> Update(
        Guid reservationId, [FromBody] UpdateReservationRequest request, CancellationToken ct)
    {
        return Ok(await _service.UpdateAsync(User.GetTenantId(), reservationId, request, ct));
    }

    [HttpPost("{reservationId:guid}/cancel")]
    [PermissionAuthorize("reservations.cancel")]
    public async Task<IActionResult> Cancel(Guid reservationId, CancellationToken ct)
    {
        await _service.CancelAsync(User.GetTenantId(), reservationId, ct);
        return NoContent();
    }
}
