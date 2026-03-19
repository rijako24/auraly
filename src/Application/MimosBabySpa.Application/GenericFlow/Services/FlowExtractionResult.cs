namespace MimosBabySpa.Application.GenericFlow.Services;

/// <summary>
/// Result of a unified extraction turn: both extracted field values and detected intention flags
/// come from a single LLM call in JSON mode.
/// WasSuccessful = false indicates the LLM call failed; the orchestrator should use
/// FlowIntentionDetector.DegradedDetect as fallback for critical intentions.
/// </summary>
public class FlowExtractionResult
{
    public Dictionary<string, string?> ExtractedFields { get; set; } = new();
    public Dictionary<string, bool> Intentions { get; set; } = new();
    public bool WasSuccessful { get; set; } = true;

    public static FlowExtractionResult Failed() =>
        new() { WasSuccessful = false };
}
