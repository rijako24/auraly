using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/service-categories")]
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
