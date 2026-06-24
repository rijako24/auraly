using MimosBabySpa.Application.Agents.Templates;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Estado acumulado de un turno del agente.
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
    public int PromptTokens { get; private set; }
    public int CompletionTokens { get; private set; }
    public int ToolCallCount { get; private set; }
    public int ConsecutiveToolErrors { get; private set; }
    public bool EscalatedToHuman { get; private set; }
    public bool RequestCompleted { get; private set; }
    public bool DirectOutboundRequested { get; private set; }
    public bool CheckoutPrepared { get; private set; }

    public IReadOnlyList<TurnFragmentEntry> FragmentEntries =>
        _fragments.Select(kv => new TurnFragmentEntry(kv.Key, kv.Value)).ToList();

    public IReadOnlyList<OutboundMessage> OutboundMessages => _outboundMessages;

    public bool ShouldAutoEscalate =>
        ConsecutiveToolErrors >= _errorEscalationThreshold;

    public void AddTokens(int prompt, int completion)
    {
        PromptTokens += prompt;
        CompletionTokens += completion;
        TotalTokens += prompt + completion;
    }

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

        if (outcome.HasEffect(ToolSideEffectNames.RequestCompleted))
            RequestCompleted = true;

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

    public void EnqueueOutbound(IEnumerable<OutboundMessage> messages) =>
        _outboundMessages.AddRange(messages);

    public void MarkDirectOutboundRequested() => DirectOutboundRequested = true;

    public bool TryMarkSequenceEnqueued(string sequenceName) =>
        _enqueuedSequences.Add(sequenceName);

    public AgentTurnResult ToSuccessResult(string response) =>
        AgentTurnResult.Ok(
            response,
            EscalatedToHuman,
            RequestCompleted,
            TotalTokens,
            ToolCallCount,
            OutboundMessages);
}