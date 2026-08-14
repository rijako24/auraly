namespace Auraly.Platform.Application.Agents.Configuration;

/// <summary>
/// Deterministic admission policy for turns received outside business operating hours.
/// The engine blocks operations; presentation behavior is tenant configuration.
/// </summary>
public sealed class OperatingHoursDefinitions
{
    public bool Enforce { get; set; }

    public OutsideOperatingHoursResponseDefinition OutsideHours { get; set; } = new();

    public bool Enabled
    {
        get => Enforce;
        set => Enforce = value;
    }
}

public sealed class OutsideOperatingHoursResponseDefinition
{
    /// <summary>Renderer-only guidance. It cannot enable operations or mutate state.</summary>
    public string Guidance { get; set; } = string.Empty;

    /// <summary>Optional exclusive template. When set, no LLM prose is used.</summary>
    public string? Template { get; set; }

    /// <summary>Optional direct outbound sequence instead of a normal response.</summary>
    public string? SendMessageSequence { get; set; }
}