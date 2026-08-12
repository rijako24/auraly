using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
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
    private readonly MimosBabySpa.Application.Services.AgentConfigProviderAccessor _configProvider;
    private readonly ILogger<AgentAdminService> _logger;

    public AgentAdminService(
        IAgentRepository agentRepository,
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        ICorrelationIdProvider correlationIdProvider,
        MimosBabySpa.Application.Services.AgentConfigProviderAccessor configProvider,
        ILogger<AgentAdminService> logger)
    {
        _agentRepository = agentRepository;
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _correlationIdProvider = correlationIdProvider;
        _configProvider = configProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AgentDto>> GetByBusinessIdAsync(
        Guid tenantId, bool canAccessAllTenants, Guid businessId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, businessId, ct);
        var agents = await _agentRepository.GetByBusinessAsync(businessId, ct);
        var whatsappNumbers = (await _unitOfWork.BusinessWhatsAppNumbers.GetByBusinessIdAsync(businessId))
            .Where(number => number.IsActive && number.AgentId.HasValue)
            .ToList();
        var inboundContacts = await _unitOfWork.BusinessInboundContacts.GetActiveByBusinessAsync(businessId, ct);
        return agents.Select(agent => MapToDto(
            agent,
            whatsappNumbers.FirstOrDefault(number => number.AgentId == agent.AgentId)?.PhoneNumber
                ?? inboundContacts.FirstOrDefault(contact => contact.InboundAgentId == agent.AgentId)?.PhoneNumber
        )).ToList();
    }

    public async Task<IReadOnlyList<BusinessInboundContactDto>> GetInboundContactsByBusinessIdAsync(
        Guid tenantId, bool canAccessAllTenants, Guid businessId, bool includeInactive = false, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, businessId, ct);
        var contacts = includeInactive
            ? await _unitOfWork.BusinessInboundContacts.GetByBusinessAsync(businessId, includeInactive: true, ct)
            : await _unitOfWork.BusinessInboundContacts.GetActiveByBusinessAsync(businessId, ct);
        return contacts.Select(MapToInboundContactDto).ToList();
    }

    public async Task<BusinessInboundContactDto> GetInboundContactByIdAsync(
        Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid contactId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, businessId, ct);
        var contact = await GetInboundContactForBusinessAsync(businessId, contactId, ct);
        return MapToInboundContactDto(contact);
    }

    public async Task<BusinessInboundContactDto> CreateInboundContactAsync(
        Guid tenantId, bool canAccessAllTenants, Guid businessId, CreateBusinessInboundContactRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, businessId, ct);
        var type = NormalizeType(request.Type);
        var name = RequireTrimmed(request.Name, "Name", "El nombre del contacto es obligatorio.");
        var role = (request.Role ?? string.Empty).Trim();
        var phoneNumber = RequireTrimmed(request.PhoneNumber, "PhoneNumber", "El telefono es obligatorio.");
        var phoneNormalized = NormalizePhone(phoneNumber);
        if (string.IsNullOrWhiteSpace(phoneNormalized))
            throw new DomainValidationException("PhoneNumber", "El telefono debe contener digitos.");

        await EnsurePhoneIsAvailableAsync(businessId, phoneNormalized, exceptContactId: null, ct);
        var inboundAgent = await ValidateInboundAgentAsync(businessId, request.InboundAgentId, type, ct);
        await ValidateEmployeeAsync(businessId, request.EmployeeId, ct);
        var capabilitiesJson = NormalizeJsonOrNull(request.CapabilitiesJson, "CapabilitiesJson");

        var contact = new BusinessInboundContact
        {
            BusinessInboundContactId = Guid.NewGuid(),
            BusinessId = businessId,
            Type = type,
            Key = NormalizeKey(request.Key, name, type),
            Name = name,
            Role = role,
            PhoneNumber = phoneNumber,
            PhoneNormalized = phoneNormalized,
            InboundAgentId = inboundAgent.AgentId,
            EmployeeId = request.EmployeeId,
            CapabilitiesJson = capabilitiesJson,
            IsActive = request.IsActive ?? true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.BusinessInboundContacts.AddAsync(contact, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Create", nameof(BusinessInboundContact), contact.BusinessInboundContactId.ToString(), null, MapToInboundContactDto(contact), ct);
        _logger.LogInformation(
            "Inbound contact {ContactId} created for business {BusinessId} [CorrelationId: {CorrelationId}]",
            contact.BusinessInboundContactId,
            businessId,
            _correlationIdProvider.CorrelationId);

        var created = await _unitOfWork.BusinessInboundContacts.GetByIdAsync(contact.BusinessInboundContactId, ct);
        return MapToInboundContactDto(created ?? contact);
    }

    public async Task<BusinessInboundContactDto> UpdateInboundContactAsync(
        Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid contactId, UpdateBusinessInboundContactRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, businessId, ct);
        var contact = await GetInboundContactForBusinessAsync(businessId, contactId, ct);
        var oldState = MapToInboundContactDto(contact);

        var type = NormalizeType(request.Type ?? contact.Type);
        var name = request.Name is null
            ? contact.Name
            : RequireTrimmed(request.Name, "Name", "El nombre del contacto es obligatorio.");
        var phoneNumber = request.PhoneNumber is null
            ? contact.PhoneNumber
            : RequireTrimmed(request.PhoneNumber, "PhoneNumber", "El telefono es obligatorio.");
        var phoneNormalized = NormalizePhone(phoneNumber);
        if (string.IsNullOrWhiteSpace(phoneNormalized))
            throw new DomainValidationException("PhoneNumber", "El telefono debe contener digitos.");

        await EnsurePhoneIsAvailableAsync(businessId, phoneNormalized, contactId, ct);
        var inboundAgentId = request.InboundAgentId ?? contact.InboundAgentId;
        var inboundAgent = await ValidateInboundAgentAsync(businessId, inboundAgentId, type, ct);
        if (request.EmployeeId.HasValue)
            await ValidateEmployeeAsync(businessId, request.EmployeeId, ct);

        contact.Type = type;
        contact.Key = request.Key is null ? contact.Key : NormalizeKey(request.Key, name, type);
        contact.Name = name;
        if (request.Role is not null) contact.Role = request.Role.Trim();
        contact.PhoneNumber = phoneNumber;
        contact.PhoneNormalized = phoneNormalized;
        contact.InboundAgentId = inboundAgent.AgentId;
        if (request.EmployeeId.HasValue) contact.EmployeeId = request.EmployeeId;
        if (request.CapabilitiesJson is not null)
            contact.CapabilitiesJson = NormalizeJsonOrNull(request.CapabilitiesJson, "CapabilitiesJson");
        if (request.IsActive.HasValue) contact.IsActive = request.IsActive.Value;
        contact.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.BusinessInboundContacts.UpdateAsync(contact, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var updated = await _unitOfWork.BusinessInboundContacts.GetByIdAsync(contactId, ct) ?? contact;
        var dto = MapToInboundContactDto(updated);
        await _auditService.LogAsync("Update", nameof(BusinessInboundContact), contactId.ToString(), oldState, dto, ct);
        return dto;
    }

    public async Task DeactivateInboundContactAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid contactId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, businessId, ct);
        var contact = await GetInboundContactForBusinessAsync(businessId, contactId, ct);
        if (!contact.IsActive)
            return;

        var oldState = MapToInboundContactDto(contact);
        contact.IsActive = false;
        contact.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.BusinessInboundContacts.UpdateAsync(contact, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _auditService.LogAsync("Deactivate", nameof(BusinessInboundContact), contactId.ToString(), oldState, MapToInboundContactDto(contact), ct);
    }

    public async Task<AgentDto> CreateAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid businessId,
        CreateAgentRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, businessId, ct);

        var name = RequireTrimmed(request.Name, "Name", "El nombre del agente es obligatorio.");
        if (name.Length > 200)
            throw new DomainValidationException("Name", "El nombre del agente no puede superar 200 caracteres.");
        if (await _agentRepository.NameExistsAsync(businessId, name, ct))
            throw new ConflictException($"Ya existe un agente con el nombre '{name}' en este negocio.");

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        if (description?.Length > 500)
            throw new DomainValidationException("Description", "La descripcion no puede superar 500 caracteres.");

        if (!Enum.IsDefined(request.BotType))
            throw new DomainValidationException("BotType", "El tipo de bot no es valido.");

        var agentType = await _agentRepository.GetDefaultTypeAsync(ct)
            ?? throw new InvalidOperationException("No existe un tipo de agente activo para crear el borrador.");
        var agent = new Agent
        {
            BusinessId = businessId,
            AgentTypeId = agentType.AgentTypeId,
            BotType = request.BotType,
            Name = name,
            Description = description,
            Kind = ResolveAgentKind(request.BotType),
            IsActive = false,
            SettingsJson = CreateDraftSettingsJson(name, request.BotType)
        };

        await _agentRepository.AddAsync(agent, ct);
        var dto = MapToDto(agent);
        await _auditService.LogAsync("Create", nameof(Agent), agent.AgentId.ToString(), null, dto, ct);
        _logger.LogInformation(
            "Draft agent {AgentId} created for business {BusinessId} [CorrelationId: {CorrelationId}]",
            agent.AgentId,
            businessId,
            _correlationIdProvider.CorrelationId);
        return dto;
    }

    public async Task<AgentDto> GetByIdAsync(Guid tenantId, bool canAccessAllTenants, Guid agentId, CancellationToken ct = default)
    {
        var agent = await GetAgentForTenantAsync(tenantId, canAccessAllTenants, agentId, ct);
        return MapToDto(agent);
    }

    public async Task<AgentDto> UpdateSettingsAsync(
        Guid tenantId, bool canAccessAllTenants, Guid agentId, UpdateAgentSettingsRequest request, CancellationToken ct = default)
    {
        var agent = await GetAgentForTenantAsync(tenantId, canAccessAllTenants, agentId, ct);
        var oldJson = agent.SettingsJson;

        var settingsJson = JsonSerializer.Serialize(request.Settings, JsonOptions);
        agent.SettingsJson = settingsJson;
        agent.UpdatedAt = DateTime.UtcNow;

        await _agentRepository.UpdateAsync(agent, ct);
        _configProvider().Invalidate(agentId);

        if (agent.IsActive)
        {
            try
            {
                await _configProvider().GetConfigForAdminAsync(agentId, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                agent.SettingsJson = oldJson;
                await _agentRepository.UpdateAsync(agent, ct);
                _configProvider().Invalidate(agentId);
                throw new DomainValidationException(
                    "Settings",
                    $"La configuracion no es valida y no fue aplicada: {exception.Message}");
            }
        }

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

    public async Task<AgentDto> UpdateStatusAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid agentId,
        UpdateAgentStatusRequest request,
        CancellationToken ct = default)
    {
        var agent = await GetAgentForTenantAsync(tenantId, canAccessAllTenants, agentId, ct);
        if (agent.IsActive == request.IsActive)
            return MapToDto(agent);

        if (request.IsActive)
        {
            try
            {
                await _configProvider().GetConfigForAdminAsync(agentId, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new DomainValidationException(
                    "Settings",
                    $"No se puede publicar el agente: {exception.Message}");
            }
        }

        var oldState = new { agent.IsActive };
        agent.IsActive = request.IsActive;
        await _agentRepository.UpdateAsync(agent, ct);
        _configProvider().Invalidate(agentId);

        var dto = MapToDto(agent);
        await _auditService.LogAsync("UpdateStatus", nameof(Agent), agentId.ToString(), oldState, new { agent.IsActive }, ct);
        return dto;
    }

    internal static string CreateDraftSettingsJson(string agentName, AgentBotType botType = AgentBotType.Reservation) =>
        JsonSerializer.Serialize(new
        {
            model = "gpt-4.1-mini",
            temperature = 0.2,
            historyWindowSize = 20,
            extractorHistoryWindowSize = 2,
            persona = $"Eres {agentName}, asistente virtual del negocio. Habla de forma clara, cordial y breve.",
            policies = "Atiende solo con informacion autorizada del negocio. Si no puedes resolver una solicitud, ofrece escalar a una persona.",
            conversationOpening = new
            {
                enabled = true,
                guidance = "Saluda brevemente, presentate y pregunta en que puedes ayudar.",
                allowQuestions = true
            },
            flows = new[]
            {
                new
                {
                    id = "main",
                    type = "primary",
                    routingGuidance = "Use this primary flow for the normal customer journey.",
                    stages = new[]
                    {
                        new
                        {
                            id = "start",
                            name = "Inicio",
                            goal = "Comprender la necesidad del cliente y orientarlo con la informacion configurada.",
                            collect = Array.Empty<string>(),
                            advanceWhenFacts = Array.Empty<string>(),
                            signals = Array.Empty<object>(),
                            actions = Array.Empty<object>(),
                            transitions = Array.Empty<object>(),
                            response = new { guidance = "Responde la solicitud y pide solo la informacion necesaria para avanzar." }
                        }
                    }
                }
            },
            factSchema = Array.Empty<object>(),
            checkout = CreateDraftCheckout(botType),
            globalActions = Array.Empty<object>(),
            templates = new Dictionary<string, string>()
        }, JsonOptions);

    private static string ResolveAgentKind(AgentBotType botType) => botType switch
    {
        AgentBotType.Delivery => "domicilio",
        AgentBotType.PaymentValidator => "payment_approval",
        _ => "customer"
    };

    private static object CreateDraftCheckout(AgentBotType botType)
    {
        var modes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (botType == AgentBotType.Order)
            modes["order"] = new { paymentMethods = new Dictionary<string, object>() };
        else if (botType == AgentBotType.Reservation)
            modes["reservation"] = new { paymentMethods = new Dictionary<string, object>() };

        return new
        {
            currency = "COP",
            modes
        };
    }

    private async Task<BusinessInboundContact> GetInboundContactForBusinessAsync(Guid businessId, Guid contactId, CancellationToken ct)
    {
        var contact = await _unitOfWork.BusinessInboundContacts.GetByIdAsync(contactId, ct)
            ?? throw new NotFoundException(nameof(BusinessInboundContact), contactId);
        if (contact.BusinessId != businessId)
            throw new NotFoundException(nameof(BusinessInboundContact), contactId);
        return contact;
    }

    private async Task<Agent> GetAgentForTenantAsync(Guid tenantId, bool canAccessAllTenants, Guid agentId, CancellationToken ct)
    {
        var agent = await _agentRepository.GetByIdForAdminAsync(agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, agent.BusinessId, ct);
        return agent;
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (!canAccessAllTenants && business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private async Task<Agent> ValidateInboundAgentAsync(Guid businessId, Guid agentId, string contactType, CancellationToken ct)
    {
        var agent = await _agentRepository.GetByIdForAdminAsync(agentId, ct)
            ?? throw new DomainValidationException("InboundAgentId", "El agente inbound no existe.");
        if (agent.BusinessId != businessId)
            throw new DomainValidationException("InboundAgentId", "El agente inbound no pertenece al negocio.");
        if (!agent.IsActive)
            throw new DomainValidationException("InboundAgentId", "El agente inbound debe estar activo.");
        if (!agent.Kind.Equals(contactType, StringComparison.OrdinalIgnoreCase))
            throw new DomainValidationException("InboundAgentId", "El tipo del contacto debe coincidir con el tipo del agente inbound.");
        if (agent.Kind.Equals("customer", StringComparison.OrdinalIgnoreCase))
            throw new DomainValidationException("InboundAgentId", "Un contacto inbound no puede usar el agente de clientes.");
        return agent;
    }

    private async Task ValidateEmployeeAsync(Guid businessId, Guid? employeeId, CancellationToken ct)
    {
        if (!employeeId.HasValue)
            return;

        var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId.Value)
            ?? throw new DomainValidationException("EmployeeId", "El empleado no existe.");
        if (employee.BusinessId != businessId)
            throw new DomainValidationException("EmployeeId", "El empleado no pertenece al negocio.");
    }

    private async Task EnsurePhoneIsAvailableAsync(Guid businessId, string phoneNormalized, Guid? exceptContactId, CancellationToken ct)
    {
        var existing = await _unitOfWork.BusinessInboundContacts.GetByPhoneAsync(businessId, phoneNormalized, ct);
        if (existing is not null && existing.BusinessInboundContactId != exceptContactId)
            throw new ConflictException("Ya existe un contacto inbound con ese telefono en este negocio.");
    }

    private static string NormalizeType(string type)
    {
        var normalized = RequireTrimmed(type, "Type", "El tipo del contacto es obligatorio.").ToLowerInvariant();
        if (normalized.Equals("customer", StringComparison.OrdinalIgnoreCase))
            throw new DomainValidationException("Type", "Un contacto inbound no puede ser de tipo customer.");
        return normalized;
    }

    private static string NormalizeKey(string? key, string name, string type)
    {
        var source = string.IsNullOrWhiteSpace(key) ? $"{type}_{name}" : key.Trim();
        var chars = source
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var normalized = string.Join('_', new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
            throw new DomainValidationException("Key", "La clave del contacto es obligatoria.");
        return normalized;
    }

    private static string NormalizePhone(string phone) => new(phone.Where(char.IsDigit).ToArray());

    private static string RequireTrimmed(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainValidationException(field, message);
        return value.Trim();
    }

    private static string? NormalizeJsonOrNull(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            using var _ = JsonDocument.Parse(value);
            return value.Trim();
        }
        catch (JsonException)
        {
            throw new DomainValidationException(field, "Debe ser un JSON valido.");
        }
    }

    private static BusinessInboundContactDto MapToInboundContactDto(BusinessInboundContact contact) => new()
    {
        BusinessInboundContactId = contact.BusinessInboundContactId,
        BusinessId = contact.BusinessId,
        Type = contact.Type,
        Key = contact.Key,
        Name = contact.Name,
        Role = contact.Role,
        PhoneNumber = contact.PhoneNumber,
        PhoneNormalized = contact.PhoneNormalized,
        InboundAgentId = contact.InboundAgentId,
        InboundAgentName = contact.InboundAgent?.Name ?? string.Empty,
        EmployeeId = contact.EmployeeId,
        CapabilitiesJson = contact.CapabilitiesJson,
        IsActive = contact.IsActive,
        CreatedAt = contact.CreatedAt,
        UpdatedAt = contact.UpdatedAt
    };

    private static AgentDto MapToDto(Agent agent, string? phoneNumber = null)
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
                /* admin mostrara JSON crudo si falla el parse */
            }
        }

        return new AgentDto
        {
            AgentId = agent.AgentId,
            BusinessId = agent.BusinessId,
            AgentTypeId = agent.AgentTypeId,
            AgentTypeName = agent.AgentType?.Name ?? string.Empty,
            BotType = agent.BotType,
            Kind = agent.Kind,
            Name = agent.Name,
            Description = agent.Description,
            IsActive = agent.IsActive,
            CreatedAt = agent.CreatedAt,
            PhoneNumber = phoneNumber,
            UpdatedAt = agent.UpdatedAt,
            SettingsJson = agent.SettingsJson,
            Settings = settings
        };
    }
}