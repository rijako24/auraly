using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IAgentAdminService
{
    Task<IReadOnlyList<AgentDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<AgentDto> GetByIdAsync(Guid tenantId, Guid agentId, CancellationToken ct = default);
    Task<AgentDto> UpdateSettingsAsync(Guid tenantId, Guid agentId, UpdateAgentSettingsRequest request, CancellationToken ct = default);
}
