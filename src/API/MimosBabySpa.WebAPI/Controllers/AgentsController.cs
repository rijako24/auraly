using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly IAgentAdminService _service;

    public AgentsController(IAgentAdminService service) => _service = service;

    [HttpGet("api/businesses/{businessId:guid}/agents")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<IReadOnlyList<AgentDto>>> GetByBusiness(Guid businessId, CancellationToken ct) =>
        Ok(await _service.GetByBusinessIdAsync(User.GetTenantId(), businessId, ct));

    [HttpGet("api/agents/{agentId:guid}")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<AgentDto>> GetById(Guid agentId, CancellationToken ct) =>
        Ok(await _service.GetByIdAsync(User.GetTenantId(), agentId, ct));

    [HttpPut("api/agents/{agentId:guid}/settings")]
    [PermissionAuthorize("agents.update")]
    public async Task<ActionResult<AgentDto>> UpdateSettings(
        Guid agentId,
        [FromBody] UpdateAgentSettingsRequest request,
        CancellationToken ct) =>
        Ok(await _service.UpdateSettingsAsync(User.GetTenantId(), agentId, request, ct));
}
