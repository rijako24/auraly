using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.WhatsAppTemplates.DTOs;
using MimosBabySpa.Application.WhatsAppTemplates.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/whatsapp-templates")]
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
