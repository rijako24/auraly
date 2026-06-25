using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/payments")]
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
