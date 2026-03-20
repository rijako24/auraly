using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IAgentAdminService
{
    Task<PagedResponse<AgentDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct);

    Task<AgentDetailDto> GetByIdAsync(Guid tenantId, Guid agentId, CancellationToken ct);

    Task<AgentDto> CreateAsync(Guid tenantId, CreateAgentRequest request, CancellationToken ct);

    Task<AgentDto> UpdateAsync(Guid tenantId, Guid agentId, UpdateAgentRequest request, CancellationToken ct);

    Task<IReadOnlyList<AgentTypeDto>> GetAgentTypesAsync(CancellationToken ct);

    Task<IReadOnlyList<FlowNodeCatalogEntryDto>> GetNodeCatalogAsync(CancellationToken ct);

    Task<FlowDefinitionAdminDto> GetWorkflowAsync(Guid tenantId, Guid agentId, CancellationToken ct);

    Task<FlowDefinitionAdminDto> SaveWorkflowAsync(
        Guid tenantId, Guid agentId, SaveWorkflowRequest request, CancellationToken ct);

    Task<IReadOnlyList<KnowledgeSourceAdminDto>> GetKnowledgeSourcesAsync(
        Guid tenantId, Guid agentId, CancellationToken ct);

    Task<KnowledgeSourceAdminDto> AddKnowledgeSourceAsync(
        Guid tenantId, Guid agentId, CreateKnowledgeSourceRequest request, CancellationToken ct);

    Task<AgentChatResponseDto> ChatAsync(
        Guid tenantId, Guid userId, Guid agentId, AgentChatRequest request, CancellationToken ct);
}
