using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/service-categories")]
[Authorize]
public class ServiceCategoriesController : ControllerBase
{
    private readonly IServiceAdminService _service;

    public ServiceCategoriesController(IServiceAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("services.read")]
    public async Task<ActionResult<PagedResponse<ServiceCategoryDto>>> GetByBusiness(
        [FromQuery] Guid businessId,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.GetPagedCategoriesByBusinessIdAsync(
            User.GetTenantId(), businessId, request, ct));
    }
}
