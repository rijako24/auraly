namespace MimosBabySpa.Domain.Models.Flow;

/// <summary>
/// Defines a routing intent at the flow level.
/// All agents inherit these intents for escape-intent detection.
/// The Router uses them for initial classification.
/// </summary>
public class FlowRoutingIntent
{
    /// <summary>
    /// Unique intent key (e.g. "user_wants_to_cancel").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description used in the LLM classification prompt.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Example phrases that trigger this intent (used in the classification prompt).
    /// </summary>
    public List<string> Examples { get; set; } = new();

    /// <summary>
    /// Regex pattern for degraded (non-LLM) fallback detection.
    /// </summary>
    public string? DegradedRegex { get; set; }
}
