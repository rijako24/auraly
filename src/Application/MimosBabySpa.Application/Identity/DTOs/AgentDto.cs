using System.Text.Json;

namespace MimosBabySpa.Application.Identity.DTOs;

public sealed class AgentDto
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public Guid AgentTypeId { get; init; }
    public string AgentTypeName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>SettingsJson crudo tal como está en BD.</summary>
    public string? SettingsJson { get; init; }

    /// <summary>SettingsJson parseado para el editor del admin.</summary>
    public JsonElement? Settings { get; init; }
}

public sealed class UpdateAgentSettingsRequest
{
    /// <summary>Documento completo de SettingsJson (persona, flow, guards, etc.).</summary>
    public JsonElement Settings { get; init; }
}
