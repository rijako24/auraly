using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public sealed class AgentAdminService : IAgentAdminService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IAgentRepository _agentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<AgentAdminService> _logger;

    public AgentAdminService(
        IAgentRepository agentRepository,
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<AgentAdminService> logger)
    {
        _agentRepository = agentRepository;
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AgentDto>> GetByBusinessIdAsync(
        Guid tenantId, Guid businessId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var agents = await _agentRepository.GetByBusinessAsync(businessId, ct);
        return agents.Select(MapToDto).ToList();
    }

    public async Task<AgentDto> GetByIdAsync(Guid tenantId, Guid agentId, CancellationToken ct = default)
    {
        var agent = await GetAgentForTenantAsync(tenantId, agentId, ct);
        return MapToDto(agent);
    }

    public async Task<AgentDto> UpdateSettingsAsync(
        Guid tenantId, Guid agentId, UpdateAgentSettingsRequest request, CancellationToken ct = default)
    {
        var agent = await GetAgentForTenantAsync(tenantId, agentId, ct);
        var oldJson = agent.SettingsJson;

        var settingsJson = JsonSerializer.Serialize(request.Settings, JsonOptions);
        agent.SettingsJson = settingsJson;
        agent.UpdatedAt = DateTime.UtcNow;

        await _agentRepository.UpdateAsync(agent, ct);

        await _auditService.LogAsync(
            "Update",
            nameof(Agent),
            agentId.ToString(),
            new { settingsJson = oldJson },
            new { settingsJson },
            ct);

        _logger.LogInformation(
            "Agent {AgentId} settings updated [CorrelationId: {CorrelationId}]",
            agentId, _correlationIdProvider.CorrelationId);

        return MapToDto(agent);
    }

    private async Task<Agent> GetAgentForTenantAsync(Guid tenantId, Guid agentId, CancellationToken ct)
    {
        var agent = await _agentRepository.GetByIdForAdminAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, agent.BusinessId, ct);
        return agent;
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static AgentDto MapToDto(Agent agent)
    {
        JsonElement? settings = null;
        if (!string.IsNullOrWhiteSpace(agent.SettingsJson))
        {
            try
            {
                settings = JsonSerializer.Deserialize<JsonElement>(agent.SettingsJson, JsonOptions);
            }
            catch
            {
                /* admin mostrará JSON crudo si falla el parse */
            }
        }

        return new AgentDto
        {
            AgentId = agent.AgentId,
            BusinessId = agent.BusinessId,
            AgentTypeId = agent.AgentTypeId,
            AgentTypeName = agent.AgentType?.Name ?? string.Empty,
            Name = agent.Name,
            Description = agent.Description,
            IsActive = agent.IsActive,
            CreatedAt = agent.CreatedAt,
            UpdatedAt = agent.UpdatedAt,
            SettingsJson = agent.SettingsJson,
            Settings = settings
        };
    }
}
