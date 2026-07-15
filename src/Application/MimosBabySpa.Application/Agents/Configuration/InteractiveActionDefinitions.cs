using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Maps an inbound interactive payload (scope:outcome:sourceId) to a deterministic operation.
/// The source id is carried by the button itself, so the action never depends on message order.
/// </summary>
public sealed class InteractiveActionDefinitions
    : Dictionary<string, Dictionary<string, InteractiveActionConfig>>
{
    public InteractiveActionDefinitions()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}

public sealed class InteractiveActionConfig
{
    public string Operation { get; set; } = string.Empty;

    public Dictionary<string, JsonElement> Arguments { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? SendMessageSequence { get; set; }
}
