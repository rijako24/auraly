using Auraly.Api.Extensions;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/branding/print")]
[Authorize]
public sealed class TenantPrintBrandingController(ITenantService tenants) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TenantBrandingDto>> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var result = await tenants.GetConditionalPrintBrandingAsync(
            User.GetTenantId(), Request.Headers.IfNoneMatch.ToString(), ct);
        Response.Headers.ETag = result.ETag;
        if (result.Branding is null)
            return StatusCode(StatusCodes.Status304NotModified);
        return Ok(result.Branding);
    }
}
