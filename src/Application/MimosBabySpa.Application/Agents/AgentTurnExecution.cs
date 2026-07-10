using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.LLM;

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
    private readonly HashSet<string> _entryActionRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AgentTurnTraceEntry> _trace = [];
    private int _fragmentRevision;
    private int _turnCompletingFragmentRevision;

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
    public int FragmentRevision => _fragmentRevision;

    public IReadOnlyList<TurnFragmentEntry> FragmentEntries =>
        _fragments.Select(kv => new TurnFragmentEntry(kv.Key, kv.Value)).ToList();

    public IReadOnlyList<OutboundMessage> OutboundMessages => _outboundMessages;
    public IReadOnlyList<AgentTurnTraceEntry> Trace => _trace;

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
        _fragmentRevision++;

        if (mode == FragmentRenderMode.Exclusive || priority == FragmentPriority.Required)
            _turnCompletingFragmentRevision = _fragmentRevision;

        return token;
    }

    public bool HasTurnCompletingFragmentSince(int previousFragmentRevision) =>
        _turnCompletingFragmentRevision > previousFragmentRevision;

    public void MarkCheckoutPrepared() => CheckoutPrepared = true;

    public void EnqueueOutbound(IEnumerable<OutboundMessage> messages) =>
        _outboundMessages.AddRange(messages);

    public void MarkDirectOutboundRequested() => DirectOutboundRequested = true;

    public bool TryMarkSequenceEnqueued(string sequenceName) =>
        _enqueuedSequences.Add(sequenceName);

    public bool HasEntryActionRun(string key) =>
        _entryActionRuns.Contains(key);

    public void MarkEntryActionRun(string key) =>
        _entryActionRuns.Add(key);

    public void RecordPromptTrace(
        int iteration,
        string? stageId,
        string systemPrompt,
        IReadOnlyList<string> enabledTools)
    {
        _trace.Add(new AgentTurnTraceEntry
        {
            Kind = "system_prompt",
            Iteration = iteration,
            StageId = stageId,
            Content = systemPrompt,
            EnabledTools = enabledTools
        });
    }

    public void RecordLlmTrace(int iteration, string? stageId, ChatCompletionResult result)
    {
        _trace.Add(new AgentTurnTraceEntry
        {
            Kind = "llm_response",
            Iteration = iteration,
            StageId = stageId,
            Content = result.Content,
            FinishReason = result.FinishReason.ToString(),
            ToolCalls = result.ToolCalls
                .Select(t => new ToolCallTraceEntry
                {
                    Id = t.Id,
                    FunctionName = t.FunctionName,
                    ArgumentsJson = t.ArgumentsJson
                })
                .ToList()
        });
    }

    public void RecordToolTrace(
        int iteration,
        string? stageId,
        ToolCallRequest toolCall,
        string llmVisibleResultJson)
    {
        _trace.Add(new AgentTurnTraceEntry
        {
            Kind = "tool_result",
            Iteration = iteration,
            StageId = stageId,
            ToolName = toolCall.FunctionName,
            ToolArgumentsJson = toolCall.ArgumentsJson,
            ToolResultJson = llmVisibleResultJson
        });
    }

    public AgentTurnResult ToSuccessResult(string response) =>
        AgentTurnResult.Ok(
            response,
            EscalatedToHuman,
            RequestCompleted,
            TotalTokens,
            ToolCallCount,
            OutboundMessages,
            Trace);
}
