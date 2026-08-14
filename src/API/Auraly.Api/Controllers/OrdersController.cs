using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Enums;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderAdminService _service;

    public OrdersController(IOrderAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("orders.read")]
    public async Task<ActionResult<PagedResponse<OrderDto>>> GetByBusiness(
        [FromQuery] Guid businessId,
        [FromQuery] string? customer,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] OrderStatus? status,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(
            User.GetTenantId(), businessId, request, customer, createdFrom, createdTo, status, ct));
    }

    [HttpGet("summary")]
    [PermissionAuthorize("orders.read")]
    public async Task<ActionResult<OrderSummaryDto>> GetSummary(
        [FromQuery] Guid businessId,
        [FromQuery] string? search,
        [FromQuery] string? customer,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] OrderStatus? status,
        CancellationToken ct)
    {
        return Ok(await _service.GetSummaryByBusinessIdAsync(
            User.GetTenantId(), businessId, search, customer, createdFrom, createdTo, status, ct));
    }

    [HttpGet("{orderId:guid}")]
    [PermissionAuthorize("orders.read")]
    public async Task<ActionResult<OrderDto>> GetById(Guid orderId, CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), orderId, ct));
    }
}
