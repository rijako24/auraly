namespace MimosBabySpa.Domain.Models.Flow;

public class FlowSessionConfig
{
    public double AbandonmentTimeoutHours { get; set; } = 8.0;
    public bool ResetOnFlowCompletion { get; set; } = true;

    /// <summary>
    /// When any of these flags is true, the session is considered complete and resets on next turn.
    /// </summary>
    public List<string> CompletionFlags { get; set; } = new();

    /// <summary>
    /// Variables preserved across session resets (e.g. customer_name, email).
    /// </summary>
    public List<string> PersistentVariables { get; set; } = new();

    /// <summary>
    /// Variables cleared on session reset (e.g. service, desired_date, reservation_id).
    /// </summary>
    public List<string> TransactionalVariables { get; set; } = new();
}

public class FlowEngineSettings
{
    public int MaxNodesPerTurn { get; set; } = 25;

    /// <summary>Number of conversation turns (user+assistant pairs) kept for extraction context.</summary>
    public int MaxConversationHistoryMessages { get; set; } = 6;

    public float ExtractionTemperature { get; set; } = 0.1f;
    public float ResponseTemperature { get; set; } = 0.7f;

    /// <summary>Max tokens for the extraction LLM call.</summary>
    public int ExtractionMaxTokens { get; set; } = 600;

    /// <summary>Minimum confidence (0–1) required to accept an extracted field.</summary>
    public double ExtractionMinConfidence { get; set; } = 0.6;

    public int ResponseMaxTokens { get; set; } = 600;
    public int ClassifyMaxTokens { get; set; } = 80;

    /// <summary>
    /// Number of consecutive failed extraction LLM calls before auto-escalating.
    /// 0 = disabled. When reached, the engine escalates and resets the counter.
    /// </summary>
    public int DegradedTurnsBeforeEscalation { get; set; } = 2;

    /// <summary>
    /// Culture code used for date/time formatting in runtime tokens (e.g. {{runtime.day_of_week}}).
    /// Defaults to "es-ES". Change to "en-US" or any .NET-supported culture as needed.
    /// </summary>
    public string DateTimeCulture { get; set; } = "es-ES";
}
