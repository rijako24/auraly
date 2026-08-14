using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AuditLogsController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    [HttpGet]
    [PermissionAuthorize("audit_logs.read")]
    public async Task<ActionResult<PagedResponse<AuditLogDto>>> GetAll(
        [FromQuery] Guid? tenantId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? entityType,
        [FromQuery] string? correlationId,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        var effectiveTenantId = tenantId ?? User.GetTenantId();
        return Ok(await _auditService.GetPagedAsync(
            effectiveTenantId, from, to, entityType, correlationId, request, ct));
    }
}
