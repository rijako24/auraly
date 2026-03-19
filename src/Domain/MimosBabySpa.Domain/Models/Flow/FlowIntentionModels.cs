namespace MimosBabySpa.Domain.Models.Flow;

/// <summary>
/// Unified intention definition. Detected as a boolean flag in the extraction LLM call —
/// no separate LLM call required. Multiple intentions can be true simultaneously.
///
/// DegradedRegex is used ONLY when extraction fails: provides deterministic fallback
/// for the 2 most critical intents (human assistance, cancel). All other intents default to false.
///
/// StageCondition gates whether the LLM is asked to evaluate this intention.
/// Format: "flag:{flagKey}" — e.g. "flag:confirmation_summary_presented"
/// When the condition is not met, the intention defaults to false and is omitted from the prompt.
/// </summary>
public class FlowIntentionSchema
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Examples { get; set; } = new();
    public string? DegradedRegex { get; set; }
    public string? StageCondition { get; set; }
    public int Priority { get; set; } = 99;
    public FlowIntentionBehavior Behavior { get; set; } = new();
}

public class FlowIntentionBehavior
{
    /// <summary>
    /// none | escalate | goto_node
    /// </summary>
    public string Action { get; set; } = "none";

    public string? TargetNodeId { get; set; }
    public string? ResponseTemplate { get; set; }
}
