namespace MimosBabySpa.Application.Identity.DTOs;

public record AgentDto(
    Guid AgentId,
    Guid BusinessId,
    string Name,
    string? Description,
    string AgentTypeName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record AgentDetailDto(
    Guid AgentId,
    Guid BusinessId,
    Guid AgentTypeId,
    string Name,
    string? Description,
    string? SettingsJson,
    bool IsActive,
    string AgentTypeName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<AgentPromptSectionDto> PromptSections,
    IReadOnlyList<KnowledgeSourceAdminDto> KnowledgeSources);

public record AgentPromptSectionDto(
    Guid AgentPromptSectionId,
    string Key,
    string Title,
    string Content,
    string InjectionPoint,
    int DisplayOrder);

public record KnowledgeSourceAdminDto(
    Guid KnowledgeSourceId,
    string Name,
    string Type,
    string Content,
    bool AutoInject,
    int DisplayOrder,
    DateTime CreatedAt);

public record AgentTypeDto(Guid AgentTypeId, string Name, string? Description);

public record FlowDefinitionAdminDto(
    Guid FlowDefinitionId,
    Guid AgentId,
    string Name,
    string? Description,
    string DefinitionJson,
    string Version,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateAgentRequest(
    Guid BusinessId,
    Guid AgentTypeId,
    string Name,
    string? Description,
    string? SettingsJson,
    string? SystemPrompt);

public record UpdateAgentRequest(
    string? Name,
    string? Description,
    string? SettingsJson,
    bool? IsActive);

public record CreateKnowledgeSourceRequest(
    string Name,
    string Type,
    string Content,
    bool AutoInject = true);

public record SaveWorkflowRequest(
    string? Name,
    string? Description,
    string DefinitionJson);

public record AgentChatRequest(string Message, bool ResetSession = false);

public record AgentChatResponseDto(
    bool Success,
    string BotResponse,
    string? ErrorMessage,
    bool IsEscalated,
    bool IsFlowComplete,
    string? CurrentNodeId,
    IReadOnlyDictionary<string, string?> Variables);

public record FlowNodeCatalogEntryDto(
    string Id,
    string Name,
    int Type,
    string Icon,
    IReadOnlyList<FlowPortDto> Inputs,
    IReadOnlyList<FlowPortDto> Outputs,
    string ConfigSchemaJson,
    string? Category,
    string? Color);

public record FlowPortDto(string Id, string Label);
