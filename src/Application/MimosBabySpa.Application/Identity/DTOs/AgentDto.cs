using System.Text.Json;

namespace MimosBabySpa.Application.Identity.DTOs;

public sealed class AgentDto
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public Guid AgentTypeId { get; init; }
    public string AgentTypeName { get; init; } = string.Empty;
    public string Kind { get; init; } = "customer";
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>SettingsJson crudo tal como está en BD.</summary>
    public string? SettingsJson { get; init; }

    /// <summary>SettingsJson parseado para el editor del admin.</summary>
    public JsonElement? Settings { get; init; }
}

public sealed class BusinessInboundContactDto
{
    public Guid BusinessInboundContactId { get; init; }
    public Guid BusinessId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string PhoneNormalized { get; init; } = string.Empty;
    public Guid InboundAgentId { get; init; }
    public string InboundAgentName { get; init; } = string.Empty;
    public Guid? EmployeeId { get; init; }
    public string? CapabilitiesJson { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record CreateBusinessInboundContactRequest(
    string Type,
    string? Key,
    string Name,
    string? Role,
    string PhoneNumber,
    Guid InboundAgentId,
    Guid? EmployeeId,
    string? CapabilitiesJson,
    bool? IsActive);

public sealed record UpdateBusinessInboundContactRequest(
    string? Type,
    string? Key,
    string? Name,
    string? Role,
    string? PhoneNumber,
    Guid? InboundAgentId,
    Guid? EmployeeId,
    string? CapabilitiesJson,
    bool? IsActive);

public sealed class UpdateAgentSettingsRequest
{
    /// <summary>Documento completo de SettingsJson (persona, flow, guards, etc.).</summary>
    public JsonElement Settings { get; init; }
}

public sealed record CreateAgentRequest(
    string Name,
    string? Description);

public sealed record UpdateAgentStatusRequest(
    bool IsActive);
