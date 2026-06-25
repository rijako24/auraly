using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Testing;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly IAgentAdminService _service;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(IAgentAdminService service, ILogger<AgentsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("api/businesses/{businessId:guid}/agents")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<IReadOnlyList<AgentDto>>> GetByBusiness(Guid businessId, CancellationToken ct) =>
        Ok(await _service.GetByBusinessIdAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, ct));

    [HttpGet("api/businesses/{businessId:guid}/inbound-contacts")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<IReadOnlyList<BusinessInboundContactDto>>> GetInboundContactsByBusiness(
        Guid businessId,
        [FromQuery] bool includeInactive,
        CancellationToken ct) =>
        Ok(await _service.GetInboundContactsByBusinessIdAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, includeInactive, ct));

    [HttpGet("api/businesses/{businessId:guid}/inbound-contacts/{contactId:guid}")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<BusinessInboundContactDto>> GetInboundContactById(Guid businessId, Guid contactId, CancellationToken ct) =>
        Ok(await _service.GetInboundContactByIdAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, contactId, ct));

    [HttpPost("api/businesses/{businessId:guid}/inbound-contacts")]
    [PermissionAuthorize("agents.update")]
    public async Task<ActionResult<BusinessInboundContactDto>> CreateInboundContact(
        Guid businessId,
        [FromBody] CreateBusinessInboundContactRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreateInboundContactAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, request, ct);
        return CreatedAtAction(nameof(GetInboundContactById), new { businessId, contactId = result.BusinessInboundContactId }, result);
    }

    [HttpPut("api/businesses/{businessId:guid}/inbound-contacts/{contactId:guid}")]
    [PermissionAuthorize("agents.update")]
    public async Task<ActionResult<BusinessInboundContactDto>> UpdateInboundContact(
        Guid businessId,
        Guid contactId,
        [FromBody] UpdateBusinessInboundContactRequest request,
        CancellationToken ct) =>
        Ok(await _service.UpdateInboundContactAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, contactId, request, ct));

    [HttpDelete("api/businesses/{businessId:guid}/inbound-contacts/{contactId:guid}")]
    [PermissionAuthorize("agents.update")]
    public async Task<IActionResult> DeactivateInboundContact(Guid businessId, Guid contactId, CancellationToken ct)
    {
        await _service.DeactivateInboundContactAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, contactId, ct);
        return NoContent();
    }

    [HttpGet("api/agents/{agentId:guid}")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<AgentDto>> GetById(Guid agentId, CancellationToken ct) =>
        Ok(await _service.GetByIdAsync(User.GetTenantId(), User.HasPermission("tenants.read"), agentId, ct));

    [HttpPut("api/agents/{agentId:guid}/settings")]
    [PermissionAuthorize("agents.update")]
    public async Task<ActionResult<AgentDto>> UpdateSettings(
        Guid agentId,
        [FromBody] UpdateAgentSettingsRequest request,
        CancellationToken ct) =>
        Ok(await _service.UpdateSettingsAsync(User.GetTenantId(), User.HasPermission("tenants.read"), agentId, request, ct));

    [HttpPost("api/agents/{agentId:guid}/test-turn")]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<AgentTestTurnResponse>> TestTurn(
        Guid agentId,
        [FromBody] AgentTestTurnRequest request,
        [FromServices] IAgentTestRuntimeFactory testRuntimeFactory,
        [FromServices] IConversationService conversationService,
        [FromServices] ApplicationDbContext db,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "El mensaje de prueba es obligatorio." });

        var agent = await _service.GetByIdAsync(User.GetTenantId(), User.HasPermission("tenants.read"), agentId, ct);
        var customerPhone = string.IsNullOrWhiteSpace(request.CustomerPhone)
            ? ($"+57000{Guid.NewGuid():N}")[..18]
            : request.CustomerPhone.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var conversation = await conversationService.GetOrCreateConversationAsync(
            agent.BusinessId,
            customerPhone,
            request.CustomerName);

        var history = request.History ?? [];
        SeedHistory(db, conversation.ConversationId, history);
        if (history.Count > 0)
            await db.SaveChangesAsync(ct);

        var log = new AgentTestExecutionLog();
        var testFacts = new Dictionary<string, string>(
            request.Facts ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        AgentTurnResult result;
        try
        {
            var testRuntime = testRuntimeFactory.Create(log, testFacts);
            result = await testRuntime.ProcessMessageAsync(
                agentId,
                conversation.ConversationId,
                request.Message,
                customerPhone,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent test turn failed for agent {AgentId}", agentId);
            await tx.RollbackAsync(ct);

            return Ok(new AgentTestTurnResponse
            {
                Success = false,
                Response = string.Empty,
                ErrorMessage = ex.Message,
                EscalatedToHuman = false,
                RequestCompleted = false,
                TotalTokens = 0,
                ToolCallCount = 0,
                Facts = testFacts,
                OutboundMessages = [],
                Events = log.Events
            });
        }

        await tx.RollbackAsync(ct);

        return Ok(new AgentTestTurnResponse
        {
            Success = result.Success,
            Response = result.Response,
            ErrorMessage = result.ErrorMessage,
            EscalatedToHuman = result.EscalatedToHuman,
            RequestCompleted = result.RequestCompleted,
            TotalTokens = result.TotalTokens,
            ToolCallCount = result.ToolCallCount,
            Facts = testFacts,
            OutboundMessages = result.OutboundMessages,
            Events = log.Events
        });
    }

    private static void SeedHistory(
        ApplicationDbContext db,
        Guid conversationId,
        IReadOnlyList<AgentTestMessageDto> history)
    {
        var now = DateTime.UtcNow;
        var messages = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(20)
            .Select((m, index) => new Message
            {
                MessageId = Guid.NewGuid(),
                ConversationId = conversationId,
                Sender = NormalizeSender(m.Role),
                MessageText = m.Content.Trim(),
                Timestamp = now.AddSeconds(index - history.Count)
            })
            .ToList();

        if (messages.Count > 0)
            db.Messages.AddRange(messages);
    }

    private static string NormalizeSender(string role) =>
        role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
            || role.Equals("bot", StringComparison.OrdinalIgnoreCase)
                ? "bot"
                : "user";
}
