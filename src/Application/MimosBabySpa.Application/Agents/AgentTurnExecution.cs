using MimosBabySpa.Application.Agents.Templates;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Estado acumulado de un turno del agente.
///
/// Reemplaza las variables mutables dispersas del bucle:
///   totalTokens, toolCallCount, escalated, reservationCreated, ConsecutiveToolErrors.
///
/// Toda actualización del estado del turno ocurre a través de este objeto,
/// centralizando la lógica de auto-escalación y side-effects.
/// </summary>
internal sealed class AgentTurnExecution
{
    private readonly int _errorEscalationThreshold;
    private readonly Dictionary<string, TurnFragment> _fragments = new(StringComparer.Ordinal);

    public AgentTurnExecution(int errorEscalationThreshold)
    {
        _errorEscalationThreshold = errorEscalationThreshold;
    }

    public int TotalTokens { get; private set; }
    public int ToolCallCount { get; private set; }
    public int ConsecutiveToolErrors { get; private set; }
    public bool EscalatedToHuman { get; private set; }
    public bool ReservationCreated { get; private set; }
    public bool CheckoutPrepared { get; private set; }

    public IReadOnlyList<TurnFragmentEntry> FragmentEntries =>
        _fragments.Select(kv => new TurnFragmentEntry(kv.Key, kv.Value)).ToList();

    public bool ShouldAutoEscalate =>
        ConsecutiveToolErrors >= _errorEscalationThreshold;

    public void AddTokens(int prompt, int completion) =>
        TotalTokens += prompt + completion;

    public void RecordToolOutcome(ToolExecutionOutcome outcome)
    {
        ToolCallCount++;

        if (outcome.IsError)
        {
            ConsecutiveToolErrors++;
            return;
        }

        ConsecutiveToolErrors = 0;

        if (outcome.HasEffect(ToolSideEffectNames.ReservationCreated))
            ReservationCreated = true;

        if (outcome.HasEffect(ToolSideEffectNames.EscalatedToHuman))
            EscalatedToHuman = true;
    }

    public void RecordToolException() => ConsecutiveToolErrors++;

    public string RegisterFragment(
        string tokenPrefix,
        string templateId,
        IReadOnlyDictionary<string, object?> data,
        FragmentRenderMode mode = FragmentRenderMode.Inline)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var token = $"{{{{{tokenPrefix}:{suffix}}}}}";
        _fragments[token] = new TurnFragment(templateId, data, mode);
        return token;
    }

    public void MarkCheckoutPrepared() => CheckoutPrepared = true;

    public AgentTurnResult ToSuccessResult(string response) =>
        AgentTurnResult.Ok(response, EscalatedToHuman, ReservationCreated, TotalTokens, ToolCallCount);
}
