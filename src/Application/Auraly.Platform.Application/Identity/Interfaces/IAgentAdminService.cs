using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IAgentAdminService
{
    Task<IReadOnlyList<AgentDto>> GetByBusinessIdAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessInboundContactDto>> GetInboundContactsByBusinessIdAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, bool includeInactive = false, CancellationToken ct = default);
    Task<BusinessInboundContactDto> GetInboundContactByIdAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid contactId, CancellationToken ct = default);
    Task<BusinessInboundContactDto> CreateInboundContactAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CreateBusinessInboundContactRequest request, CancellationToken ct = default);
    Task<BusinessInboundContactDto> UpdateInboundContactAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid contactId, UpdateBusinessInboundContactRequest request, CancellationToken ct = default);
    Task DeactivateInboundContactAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid contactId, CancellationToken ct = default);
    Task<AgentDto> CreateAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CreateAgentRequest request, CancellationToken ct = default);
    Task<AgentDto> GetByIdAsync(Guid tenantId, bool canAccessAllTenants, Guid agentId, CancellationToken ct = default);
    Task<AgentDto> UpdateSettingsAsync(Guid tenantId, bool canAccessAllTenants, Guid agentId, UpdateAgentSettingsRequest request, CancellationToken ct = default);
    Task<AgentDto> UpdateStatusAsync(Guid tenantId, bool canAccessAllTenants, Guid agentId, UpdateAgentStatusRequest request, CancellationToken ct = default);
}
