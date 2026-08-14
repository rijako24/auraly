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
[Route("api/v1/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentAdminService _service;

    public PaymentsController(IPaymentAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("payments.read")]
    public async Task<ActionResult<PagedResponse<PaymentTransactionDto>>> GetByBusiness(
        [FromQuery] Guid businessId,
        [FromQuery] PaymentTransactionStatus? status,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(
            User.GetTenantId(), businessId, request, status, ct));
    }

    [HttpGet("{paymentTransactionId:guid}")]
    [PermissionAuthorize("payments.read")]
    public async Task<ActionResult<PaymentTransactionDto>> GetById(
        Guid paymentTransactionId,
        CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), paymentTransactionId, ct));
    }

    [HttpPost("{paymentTransactionId:guid}/confirm-manual")]
    [PermissionAuthorize("payments.confirm_manual")]
    public async Task<ActionResult<PaymentTransactionDto>> ConfirmManual(
        Guid paymentTransactionId,
        CancellationToken ct)
    {
        return Ok(await _service.ConfirmManualAsync(
            User.GetTenantId(),
            User.GetUserId(),
            paymentTransactionId,
            ct));
    }
}
