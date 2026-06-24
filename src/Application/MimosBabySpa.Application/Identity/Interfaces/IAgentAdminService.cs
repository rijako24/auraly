using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IAgentAdminService
{
    Task<IReadOnlyList<AgentDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessInboundContactDto>> GetInboundContactsByBusinessIdAsync(Guid tenantId, Guid businessId, bool includeInactive = false, CancellationToken ct = default);
    Task<BusinessInboundContactDto> GetInboundContactByIdAsync(Guid tenantId, Guid businessId, Guid contactId, CancellationToken ct = default);
    Task<BusinessInboundContactDto> CreateInboundContactAsync(Guid tenantId, Guid businessId, CreateBusinessInboundContactRequest request, CancellationToken ct = default);
    Task<BusinessInboundContactDto> UpdateInboundContactAsync(Guid tenantId, Guid businessId, Guid contactId, UpdateBusinessInboundContactRequest request, CancellationToken ct = default);
    Task DeactivateInboundContactAsync(Guid tenantId, Guid businessId, Guid contactId, CancellationToken ct = default);
    Task<AgentDto> GetByIdAsync(Guid tenantId, Guid agentId, CancellationToken ct = default);
    Task<AgentDto> UpdateSettingsAsync(Guid tenantId, Guid agentId, UpdateAgentSettingsRequest request, CancellationToken ct = default);
}
