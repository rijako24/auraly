using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.WhatsAppTemplates.DTOs;
using Auraly.Platform.Application.WhatsAppTemplates.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/whatsapp-templates")]
[Authorize]
public class WhatsAppTemplatesController : ControllerBase
{
    private readonly IWhatsAppTemplateService _templates;
    private readonly IUnitOfWork _unitOfWork;

    public WhatsAppTemplatesController(
        IWhatsAppTemplateService templates,
        IUnitOfWork unitOfWork)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [PermissionAuthorize("campaigns.read")]
    public async Task<ActionResult<IReadOnlyList<WhatsAppTemplateDto>>> GetByBusiness(
        [FromQuery] Guid businessId,
        [FromQuery] bool approvedOnly = true,
        CancellationToken ct = default)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);

        if (!User.HasPermission("tenants.read") && business.TenantId != User.GetTenantId())
            throw new NotFoundException(nameof(Business), businessId);

        return Ok(await _templates.GetByBusinessIdAsync(businessId, approvedOnly, ct));
    }
}
