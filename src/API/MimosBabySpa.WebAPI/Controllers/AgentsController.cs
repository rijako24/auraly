using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/agents")]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly IAgentAdminService _service;

    public AgentsController(IAgentAdminService service) => _service = service;

    [HttpGet]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<PagedResponse<AgentDto>>> GetByBusiness(
        [FromQuery] Guid businessId,
        [FromQuery] PagedRequest request,
        CancellationToken ct) =>
        Ok(await _service.GetPagedByBusinessIdAsync(User.GetTenantId(), businessId, request, ct));

    [HttpGet("types")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<IReadOnlyList<AgentTypeDto>>> GetTypes(CancellationToken ct) =>
        Ok(await _service.GetAgentTypesAsync(ct));

    [HttpGet("node-catalog")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<IReadOnlyList<FlowNodeCatalogEntryDto>>> GetNodeCatalog(CancellationToken ct) =>
        Ok(await _service.GetNodeCatalogAsync(ct));

    [HttpGet("{agentId:guid}")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<AgentDetailDto>> GetById(Guid agentId, CancellationToken ct) =>
        Ok(await _service.GetByIdAsync(User.GetTenantId(), agentId, ct));

    [HttpPost]
    [PermissionAuthorize("agents.write")]
    public async Task<ActionResult<AgentDto>> Create([FromBody] CreateAgentRequest request, CancellationToken ct) =>
        Ok(await _service.CreateAsync(User.GetTenantId(), request, ct));

    [HttpPut("{agentId:guid}")]
    [PermissionAuthorize("agents.write")]
    public async Task<ActionResult<AgentDto>> Update(
        Guid agentId,
        [FromBody] UpdateAgentRequest request,
        CancellationToken ct) =>
        Ok(await _service.UpdateAsync(User.GetTenantId(), agentId, request, ct));

    [HttpGet("{agentId:guid}/knowledge")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<IReadOnlyList<KnowledgeSourceAdminDto>>> GetKnowledge(
        Guid agentId,
        CancellationToken ct) =>
        Ok(await _service.GetKnowledgeSourcesAsync(User.GetTenantId(), agentId, ct));

    [HttpPost("{agentId:guid}/knowledge")]
    [PermissionAuthorize("agents.write")]
    public async Task<ActionResult<KnowledgeSourceAdminDto>> AddKnowledge(
        Guid agentId,
        [FromBody] CreateKnowledgeSourceRequest request,
        CancellationToken ct) =>
        Ok(await _service.AddKnowledgeSourceAsync(User.GetTenantId(), agentId, request, ct));

    [HttpGet("{agentId:guid}/workflow")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<FlowDefinitionAdminDto>> GetWorkflow(Guid agentId, CancellationToken ct) =>
        Ok(await _service.GetWorkflowAsync(User.GetTenantId(), agentId, ct));

    [HttpPut("{agentId:guid}/workflow")]
    [PermissionAuthorize("agents.write")]
    public async Task<ActionResult<FlowDefinitionAdminDto>> SaveWorkflow(
        Guid agentId,
        [FromBody] SaveWorkflowRequest request,
        CancellationToken ct) =>
        Ok(await _service.SaveWorkflowAsync(User.GetTenantId(), agentId, request, ct));

    [HttpPost("{agentId:guid}/chat")]
    [PermissionAuthorize("agents.write")]
    public async Task<ActionResult<AgentChatResponseDto>> Chat(
        Guid agentId,
        [FromBody] AgentChatRequest request,
        CancellationToken ct) =>
        Ok(await _service.ChatAsync(User.GetTenantId(), User.GetUserId(), agentId, request, ct));

    [HttpPost("{agentId:guid}/test")]
    [PermissionAuthorize("agents.write")]
    public async Task<ActionResult<AgentChatResponseDto>> Test(
        Guid agentId,
        [FromBody] AgentChatRequest request,
        CancellationToken ct) =>
        Ok(await _service.ChatAsync(User.GetTenantId(), User.GetUserId(), agentId, request, ct));

    [HttpPost("{agentId:guid}/execute")]
    [PermissionAuthorize("agents.write")]
    public async Task<ActionResult<AgentChatResponseDto>> Execute(
        Guid agentId,
        [FromBody] AgentChatRequest request,
        CancellationToken ct) =>
        Ok(await _service.ChatAsync(User.GetTenantId(), User.GetUserId(), agentId, request, ct));
}
