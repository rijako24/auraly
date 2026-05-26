namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Estado acumulado de un turno del FlowEngine.
/// Centraliza métricas y side-effects; ya no gestiona fragments.
/// </summary>
public sealed class AgentTurnExecution
{
    private readonly int _errorEscalationThreshold;
    private readonly HashSet<string> _successfulToolNames = new(StringComparer.OrdinalIgnoreCase);

    public AgentTurnExecution(int errorEscalationThreshold)
    {
        _errorEscalationThreshold = errorEscalationThreshold;
    }

    public int TotalTokens { get; set; }
    public int ToolCallCount { get; set; }
    public int ConsecutiveToolErrors { get; private set; }
    public bool EscalatedToHuman { get; private set; }
    public bool ReservationCreated { get; private set; }

    public IReadOnlySet<string> SuccessfulToolNames => _successfulToolNames;

    public bool ShouldAutoEscalate =>
        ConsecutiveToolErrors >= _errorEscalationThreshold;

    public void AddTokens(int prompt, int completion) =>
        TotalTokens += prompt + completion;

    public void RecordToolOutcome(ToolExecutionOutcome outcome, string toolName)
    {
        ToolCallCount++;
        if (outcome.IsError)
        {
            ConsecutiveToolErrors++;
            return;
        }

        ConsecutiveToolErrors = 0;
        _successfulToolNames.Add(toolName);

        if (outcome.HasEffect(ToolSideEffectNames.ReservationCreated))
            ReservationCreated = true;

        if (outcome.HasEffect(ToolSideEffectNames.EscalatedToHuman))
            EscalatedToHuman = true;
    }

    public void RecordToolException() => ConsecutiveToolErrors++;

    public AgentTurnResult ToSuccessResult(string response) =>
        AgentTurnResult.Ok(response, EscalatedToHuman, ReservationCreated, TotalTokens, ToolCallCount);
}
