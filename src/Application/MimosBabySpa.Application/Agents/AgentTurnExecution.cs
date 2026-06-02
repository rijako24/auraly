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
    private readonly List<OutboundMessage> _outboundMessages = [];
    private readonly HashSet<string> _enqueuedSequences = new(StringComparer.OrdinalIgnoreCase);

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

    public IReadOnlyList<OutboundMessage> OutboundMessages => _outboundMessages;

    public bool ShouldAutoEscalate =>
        ConsecutiveToolErrors >= _errorEscalationThreshold;

    public void AddTokens(int prompt, int completion) =>
        TotalTokens += prompt + completion;

    public void RecordToolOutcome(ToolExecutionOutcome outcome)
    {
        ToolCallCount++;

        if (outcome.IsError)
        {
            if (!outcome.IsRecoverableError)
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
        FragmentRenderMode mode = FragmentRenderMode.Inline,
        FragmentPriority priority = FragmentPriority.Optional)
    {
        if (mode == FragmentRenderMode.Exclusive)
        {
            var stale = _fragments
                .Where(kv => kv.Value.Mode == FragmentRenderMode.Exclusive
                             && kv.Value.TemplateId.Equals(templateId, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in stale)
                _fragments.Remove(key);
        }

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var token = $"{{{{{tokenPrefix}:{suffix}}}}}";
        _fragments[token] = new TurnFragment(templateId, data, mode, priority);
        return token;
    }

    public void MarkCheckoutPrepared() => CheckoutPrepared = true;

    /// <summary>Encola mensajes outbound para envío tras la respuesta principal del turno.</summary>
    public void EnqueueOutbound(IEnumerable<OutboundMessage> messages) =>
        _outboundMessages.AddRange(messages);

    /// <summary>Evita encolar la misma secuencia dos veces en un turno.</summary>
    public bool TryMarkSequenceEnqueued(string sequenceName) =>
        _enqueuedSequences.Add(sequenceName);

    public AgentTurnResult ToSuccessResult(string response) =>
        AgentTurnResult.Ok(
            response,
            EscalatedToHuman,
            ReservationCreated,
            TotalTokens,
            ToolCallCount,
            OutboundMessages);
}
