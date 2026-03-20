using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.GenericFlow;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class AgentAdminService : IAgentAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationService _conversationService;
    private readonly IMessageService _messageService;
    private readonly IFlowOrchestrationService _flowOrchestrator;
    private readonly IAuditService _auditService;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<AgentAdminService> _logger;

    private static readonly JsonSerializerOptions FlowJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AgentAdminService(
        IUnitOfWork unitOfWork,
        IConversationService conversationService,
        IMessageService messageService,
        IFlowOrchestrationService flowOrchestrator,
        IAuditService auditService,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<AgentAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _conversationService = conversationService;
        _messageService = messageService;
        _flowOrchestrator = flowOrchestrator;
        _auditService = auditService;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<PagedResponse<AgentDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (items, totalCount) = await _unitOfWork.Agents.GetPagedByBusinessAsync(
            businessId, request.Page, request.PageSize, request.Search, ct);

        return new PagedResponse<AgentDto>(
            items.Select(MapToListDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<AgentDetailDto> GetByIdAsync(Guid tenantId, Guid agentId, CancellationToken ct)
    {
        var agent = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, agent.BusinessId, ct);
        return MapToDetailDto(agent);
    }

    public async Task<AgentDto> CreateAsync(Guid tenantId, CreateAgentRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, request.BusinessId, ct);

        var typeExists = await _unitOfWork.AgentTypes.GetActiveAsync(ct);
        if (typeExists.All(t => t.AgentTypeId != request.AgentTypeId))
            throw new NotFoundException(nameof(AgentType), request.AgentTypeId);

        if (await _unitOfWork.Agents.ExistsByBusinessAndNameAsync(request.BusinessId, request.Name.Trim(), null, ct))
            throw new ConflictException($"Ya existe un agente con el nombre '{request.Name.Trim()}' en este negocio.");

        var agent = new Agent
        {
            AgentId = Guid.NewGuid(),
            BusinessId = request.BusinessId,
            AgentTypeId = request.AgentTypeId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            SettingsJson = request.SettingsJson,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            agent.PromptSections.Add(new AgentPromptSection
            {
                AgentPromptSectionId = Guid.NewGuid(),
                AgentId = agent.AgentId,
                Key = "system_instructions",
                Title = "Instrucciones del sistema",
                Content = request.SystemPrompt.Trim(),
                InjectionPoint = "before_instructions",
                DisplayOrder = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _unitOfWork.Agents.AddAsync(agent, ct);

        var flow = new FlowDefinitionEntity
        {
            FlowDefinitionId = Guid.NewGuid(),
            AgentId = agent.AgentId,
            Name = "Default",
            Description = "Flujo inicial",
            DefinitionJson = AgentFlowDefaults.BuildMinimalDefinitionJson(),
            Version = "1.0",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.FlowDefinitions.AddAsync(flow, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Create", "Agent", agent.AgentId.ToString(), null, agent, ct);
        _logger.LogInformation(
            "Agent '{Name}' created for business {BusinessId} [CorrelationId: {CorrelationId}]",
            agent.Name, request.BusinessId, _correlationIdProvider.CorrelationId);

        var created = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agent.AgentId, ct);
        return MapToListDto(created!);
    }

    public async Task<AgentDto> UpdateAsync(Guid tenantId, Guid agentId, UpdateAgentRequest request, CancellationToken ct)
    {
        var agent = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, agent.BusinessId, ct);

        var oldState = MapToListDto(agent);

        if (!string.IsNullOrWhiteSpace(request.Name) && !string.Equals(request.Name.Trim(), agent.Name, StringComparison.Ordinal))
        {
            if (await _unitOfWork.Agents.ExistsByBusinessAndNameAsync(agent.BusinessId, request.Name.Trim(), agentId, ct))
                throw new ConflictException($"Ya existe un agente con el nombre '{request.Name.Trim()}' en este negocio.");
            agent.Name = request.Name.Trim();
        }

        if (request.Description != null)
            agent.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        if (request.SettingsJson != null)
            agent.SettingsJson = request.SettingsJson;

        if (request.IsActive.HasValue)
            agent.IsActive = request.IsActive.Value;

        await _unitOfWork.Agents.UpdateAsync(agent, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Update", "Agent", agentId.ToString(), oldState, agent, ct);

        var updated = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agentId, ct);
        return MapToListDto(updated!);
    }

    public async Task<IReadOnlyList<AgentTypeDto>> GetAgentTypesAsync(CancellationToken ct)
    {
        var types = await _unitOfWork.AgentTypes.GetActiveAsync(ct);
        return types.Select(t => new AgentTypeDto(t.AgentTypeId, t.Name, t.Description)).ToList();
    }

    public async Task<IReadOnlyList<FlowNodeCatalogEntryDto>> GetNodeCatalogAsync(CancellationToken ct)
    {
        var rows = await _unitOfWork.FlowNodeCatalog.GetActiveOrderedAsync(ct);
        var list = new List<FlowNodeCatalogEntryDto>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                list.Add(MapFlowNodeCatalogRow(row));
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "FlowNodeCatalog '{CatalogKey}' tiene JSON de puertos inválido", row.CatalogKey);
            }
        }

        if (list.Count == 0)
        {
            _logger.LogWarning(
                "Catálogo de nodos vacío. Publica la tabla FlowNodeCatalog y ejecuta Scripts/035_FlowNodeCatalog.sql.");
        }

        return list;
    }

    public async Task<FlowDefinitionAdminDto> GetWorkflowAsync(Guid tenantId, Guid agentId, CancellationToken ct)
    {
        var agent = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, agent.BusinessId, ct);

        var flow = await _unitOfWork.FlowDefinitions.GetActiveByAgentAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(FlowDefinitionEntity), agentId);

        return MapFlowDto(flow);
    }

    public async Task<FlowDefinitionAdminDto> SaveWorkflowAsync(
        Guid tenantId, Guid agentId, SaveWorkflowRequest request, CancellationToken ct)
    {
        var agent = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, agent.BusinessId, ct);

        try
        {
            if (JsonSerializer.Deserialize<FlowDefinitionDocument>(request.DefinitionJson, FlowJsonOpts) is null)
                throw new JsonException("Null document");
        }
        catch (JsonException ex)
        {
            throw new DomainValidationException("definitionJson", $"JSON de flujo inválido: {ex.Message}");
        }

        var flow = await _unitOfWork.FlowDefinitions.GetActiveByAgentAsync(agentId, ct);
        if (flow == null)
        {
            flow = new FlowDefinitionEntity
            {
                FlowDefinitionId = Guid.NewGuid(),
                AgentId = agentId,
                Name = string.IsNullOrWhiteSpace(request.Name) ? "Default" : request.Name.Trim(),
                Description = request.Description?.Trim(),
                DefinitionJson = request.DefinitionJson,
                Version = "1.0",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.FlowDefinitions.AddAsync(flow, ct);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.Name))
                flow.Name = request.Name.Trim();
            if (request.Description != null)
                flow.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            flow.DefinitionJson = request.DefinitionJson;
            await _unitOfWork.FlowDefinitions.UpdateAsync(flow, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Update", "FlowDefinition", flow.FlowDefinitionId.ToString(), null, flow, ct);

        var reloaded = await _unitOfWork.FlowDefinitions.GetByIdAsync(flow.FlowDefinitionId, ct);
        return MapFlowDto(reloaded!);
    }

    public async Task<IReadOnlyList<KnowledgeSourceAdminDto>> GetKnowledgeSourcesAsync(
        Guid tenantId, Guid agentId, CancellationToken ct)
    {
        var agent = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, agent.BusinessId, ct);

        return agent.KnowledgeSources
            .OrderBy(aks => aks.DisplayOrder)
            .Select(aks => MapKnowledgeDto(aks))
            .ToList();
    }

    public async Task<KnowledgeSourceAdminDto> AddKnowledgeSourceAsync(
        Guid tenantId, Guid agentId, CreateKnowledgeSourceRequest request, CancellationToken ct)
    {
        var agent = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, agent.BusinessId, ct);

        if (!Enum.TryParse<KnowledgeSourceType>(request.Type, ignoreCase: true, out var ksType))
            throw new DomainValidationException("type", $"Tipo de conocimiento no válido: {request.Type}");

        var ks = new KnowledgeSource
        {
            KnowledgeSourceId = Guid.NewGuid(),
            BusinessId = agent.BusinessId,
            Type = ksType,
            Name = request.Name.Trim(),
            Content = request.Content ?? string.Empty,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.KnowledgeSources.AddAsync(ks, ct);

        var maxOrder = agent.KnowledgeSources.Count != 0
            ? agent.KnowledgeSources.Max(x => x.DisplayOrder)
            : -1;

        var link = new AgentKnowledgeSource
        {
            AgentKnowledgeSourceId = Guid.NewGuid(),
            AgentId = agentId,
            KnowledgeSourceId = ks.KnowledgeSourceId,
            AutoInject = request.AutoInject,
            DisplayOrder = maxOrder + 1
        };

        await _unitOfWork.Agents.AddKnowledgeLinkAsync(link, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Create", "KnowledgeSource", ks.KnowledgeSourceId.ToString(), null, ks, ct);

        var junction = new AgentKnowledgeSource
        {
            AgentKnowledgeSourceId = link.AgentKnowledgeSourceId,
            AgentId = agentId,
            KnowledgeSourceId = ks.KnowledgeSourceId,
            AutoInject = link.AutoInject,
            DisplayOrder = link.DisplayOrder,
            KnowledgeSource = ks
        };

        return MapKnowledgeDto(junction);
    }

    public async Task<AgentChatResponseDto> ChatAsync(
        Guid tenantId, Guid userId, Guid agentId, AgentChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new DomainValidationException("message", "El mensaje no puede estar vacío.");

        var agent = await _unitOfWork.Agents.GetByIdIncludingInactiveAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, agent.BusinessId, ct);

        if (!agent.IsActive)
            throw new DomainValidationException("agent", "El agente está inactivo.");

        var userIdentifier = $"playground:{userId:D}:{agentId:D}";

        if (request.ResetSession)
            await _unitOfWork.FlowExecutionStates.DeleteAsync(agent.BusinessId, userIdentifier, agentId, ct);

        var conversation = await _conversationService.GetOrCreateConversationAsync(
            agent.BusinessId, userIdentifier, customerName: "Playground", agentId);

        var result = await _flowOrchestrator.ProcessTurnAsync(
            conversation.ConversationId, agentId, userIdentifier, request.Message.Trim(), ct);

        if (!string.IsNullOrWhiteSpace(result.BotResponse))
        {
            await _messageService.SaveMessageAsync(conversation.ConversationId, "User", request.Message.Trim());
            await _messageService.SaveMessageAsync(conversation.ConversationId, "Bot", result.BotResponse);
        }
        else if (!string.IsNullOrWhiteSpace(request.Message))
        {
            await _messageService.SaveMessageAsync(conversation.ConversationId, "User", request.Message.Trim());
        }

        return new AgentChatResponseDto(
            result.Success,
            result.BotResponse,
            result.ErrorMessage,
            result.IsEscalated,
            result.IsFlowComplete,
            result.CurrentNodeId,
            result.Variables);
    }

    private FlowNodeCatalogEntryDto MapFlowNodeCatalogRow(FlowNodeCatalog row)
    {
        var inputs = JsonSerializer.Deserialize<List<FlowPortDto>>(row.InputsJson, FlowJsonOpts) ?? [];
        var outputs = JsonSerializer.Deserialize<List<FlowPortDto>>(row.OutputsJson, FlowJsonOpts) ?? [];
        return new FlowNodeCatalogEntryDto(
            row.CatalogKey,
            row.Name,
            row.FlowNodeType,
            row.Icon,
            inputs,
            outputs,
            row.ConfigSchemaJson,
            row.Category,
            row.Color);
    }

    private static AgentDto MapToListDto(Agent a) =>
        new(
            a.AgentId,
            a.BusinessId,
            a.Name,
            a.Description,
            a.AgentType?.Name ?? string.Empty,
            a.IsActive,
            a.CreatedAt,
            a.UpdatedAt);

    private static AgentDetailDto MapToDetailDto(Agent a) =>
        new(
            a.AgentId,
            a.BusinessId,
            a.AgentTypeId,
            a.Name,
            a.Description,
            a.SettingsJson,
            a.IsActive,
            a.AgentType?.Name ?? string.Empty,
            a.CreatedAt,
            a.UpdatedAt,
            a.PromptSections
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new AgentPromptSectionDto(
                    p.AgentPromptSectionId,
                    p.Key,
                    p.Title,
                    p.Content,
                    p.InjectionPoint,
                    p.DisplayOrder))
                .ToList(),
            a.KnowledgeSources
                .OrderBy(k => k.DisplayOrder)
                .Select(MapKnowledgeDto)
                .ToList());

    private static KnowledgeSourceAdminDto MapKnowledgeDto(AgentKnowledgeSource aks) =>
        new(
            aks.KnowledgeSource.KnowledgeSourceId,
            aks.KnowledgeSource.Name,
            aks.KnowledgeSource.Type.ToString(),
            aks.KnowledgeSource.Content,
            aks.AutoInject,
            aks.DisplayOrder,
            aks.KnowledgeSource.CreatedAt);

    private static FlowDefinitionAdminDto MapFlowDto(FlowDefinitionEntity f) =>
        new(
            f.FlowDefinitionId,
            f.AgentId,
            f.Name,
            f.Description,
            f.DefinitionJson,
            f.Version,
            f.IsActive,
            f.CreatedAt,
            f.UpdatedAt);

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
