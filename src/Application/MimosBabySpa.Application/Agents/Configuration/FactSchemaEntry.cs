namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class FactSchemaEntry
{
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Rol semántico universal (ej. "customer.name", "booking.service").
    /// </summary>
    public string? Role { get; init; }

    /// <summary>Etiqueta legible para el LLM (ej. "edad del bebé").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>string | number | date | time | phone | email</summary>
    public string Type { get; init; } = "string";

    public bool Required { get; init; }

    /// <summary>user | channel | system | session</summary>
    public string Source { get; init; } = "user";

    public bool PersistsAcrossConversations { get; init; }

    /// <summary>Rango opcional para type number.</summary>
    public FactNumericRange? Range { get; init; }
}
