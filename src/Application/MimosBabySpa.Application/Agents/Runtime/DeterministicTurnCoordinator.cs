using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed class DeterministicTurnRequest
{
    public AgentConfig Config { get; init; } = null!;
    public OperationContext OperationContext { get; init; } = null!;
    public IReadOnlyDictionary<string, string> CurrentFacts { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, long> FactVersions { get; init; }
        = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    public ISet<string> ExecutedActionKeys { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string? CurrentFlowId { get; init; }
    public string? CurrentStageId { get; init; }
    public string? ActiveFlowId { get; init; }
    public bool HasOpenPrimaryRequest { get; init; }
    public string LatestUserMessage { get; init; } = string.Empty;
    public IReadOnlyList<ChatMessage> RecentConversation { get; init; } = [];
}

public sealed class DeterministicTurnResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public TurnPlan? Plan { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public FlowRouteDecision? Route { get; init; }
    public IReadOnlyList<string> VisitedStages { get; init; } = [];
    public string? CurrentStageId { get; init; }
    public IReadOnlyDictionary<string, string> Facts { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, long> FactVersions { get; init; }
        = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<StageOperationTrace> Trace { get; init; } = [];
    public IReadOnlyList<OperationPresentation> Presentations { get; init; } = [];
    public IReadOnlyList<OperationEffect> OperationEffects { get; init; } = [];
    public IReadOnlyList<string> Sequences { get; init; } = [];
    public IReadOnlyList<string> Events { get; init; } = [];
    public IReadOnlyList<OperationEvent> DomainEvents { get; init; } = [];
    public bool EscalateToHuman { get; init; }
    public bool RequestCompleted { get; init; }
    public StageResponseDefinition? Response { get; init; }
}

public sealed class DeterministicTurnCoordinator
{
    private readonly ITurnPlanner _planner;
    private readonly IDeterministicFlowSelector _flows;
    private readonly FactMutationBatchProcessor _facts;
    private readonly IConversationFactsService _factStore;
    private readonly IConversationVerificationService _verificationStore;
    private readonly DeterministicStageExecutor _stages;
    private readonly DeterministicStageTransitionResolver _transitions;

    public DeterministicTurnCoordinator(
        ITurnPlanner planner,
        IDeterministicFlowSelector flows,
        FactMutationBatchProcessor facts,
        IConversationFactsService factStore,
        IConversationVerificationService verificationStore,
        DeterministicStageExecutor stages,
        DeterministicStageTransitionResolver transitions)
    {
        _planner = planner;
        _flows = flows;
        _facts = facts;
        _factStore = factStore;
        _verificationStore = verificationStore;
        _stages = stages;
        _transitions = transitions;
    }

    public async Task<DeterministicTurnResult> ExecuteAsync(
        DeterministicTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        var planningStage = ResolvePlanningStage(request);
        if (planningStage is null)
            return Failure("No stage is available for turn planning.", request.CurrentFacts);

        var scope = TurnPlanScopeBuilder.Build(
            request.Config,
            planningStage,
            request.CurrentFacts,
            request.ActiveFlowId);
        var proposal = await _planner.PlanAsync(
            new TurnPlanningContext(
                request.Config,
                planningStage,
                scope,
                request.CurrentFacts,
                request.LatestUserMessage,
                request.OperationContext.BusinessNow,
                request.RecentConversation),
            cancellationToken);
        if (!proposal.Success || proposal.Plan is null)
            return Failure(proposal.Errors, request.CurrentFacts, proposal.Plan);

        var route = _flows.Select(
            request.Config,
            proposal.Plan,
            new FlowSelectionContext(request.ActiveFlowId, request.HasOpenPrimaryRequest));
        var flow = AgentFlowCatalog.Find(request.Config, route.ActiveFlowId);
        if (flow is null || flow.Stages.Count == 0)
            return Failure($"Selected flow '{route.ActiveFlowId}' has no stages.", request.CurrentFacts, proposal.Plan, route);

        var schema = request.Config.FactSchema.ToDictionary(fact => fact.Key, StringComparer.OrdinalIgnoreCase);
        var factBatch = _facts.Apply(schema, proposal.Plan.Facts, request.CurrentFacts, request.FactVersions);
        await PersistFactsAsync(request, schema, factBatch.Mutations, cancellationToken);

        var currentFacts = new Dictionary<string, string>(factBatch.NextFacts, StringComparer.OrdinalIgnoreCase);
        var versions = new Dictionary<string, long>(factBatch.Versions, StringComparer.OrdinalIgnoreCase);
        var changedFacts = new HashSet<string>(factBatch.ChangedFacts, StringComparer.OrdinalIgnoreCase);
        var verifications = ResolveActiveVerifications(request, currentFacts);
        var signals = TurnPlanRuntimeMapper.ToSemanticSignals(proposal.Plan);
        var selectedStage = ResolveSelectedStage(request, flow, route);

        var visited = new List<string>();
        var trace = new List<StageOperationTrace>();
        var presentations = new List<OperationPresentation>();
        var operationEffects = new List<OperationEffect>();
        var sequences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domainEvents = new List<OperationEvent>();
        var escalate = false;
        var completed = false;
        StageResponseDefinition? response = null;
        var seenStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var hop = 0; hop <= flow.Stages.Count; hop++)
        {
            if (!seenStages.Add(selectedStage.Id))
                return Failure($"Stage transition cycle detected at '{selectedStage.Id}'.", currentFacts, proposal.Plan, route);
            visited.Add(selectedStage.Id);

            var entering = hop > 0
                || !selectedStage.Id.Equals(request.CurrentStageId, StringComparison.OrdinalIgnoreCase)
                || !flow.Id.Equals(request.CurrentFlowId, StringComparison.OrdinalIgnoreCase);
            var triggers = new List<string>();
            if (entering)
                triggers.Add(StageActionTriggers.OnEnter);
            if (signals.Count > 0)
                triggers.Add(StageActionTriggers.OnSignal);
            if (changedFacts.Count > 0)
                triggers.Add(StageActionTriggers.OnFactChanged);
            triggers.Add(StageActionTriggers.WhenReady);

            foreach (var trigger in triggers)
            {
                var executionContext = new DeterministicStageExecutionContext
                {
                    OperationContext = CopyOperationContext(request.OperationContext, currentFacts),
                    Facts = currentFacts,
                    Signals = signals,
                LatestUserMessage = request.LatestUserMessage,
                    ChangedFacts = changedFacts,
                    ActiveVerifications = verifications,
                    ExecutedActionKeys = request.ExecutedActionKeys
                };
                var execution = await _stages.ExecuteAsync(selectedStage, trigger, executionContext, cancellationToken);
                trace.AddRange(execution.Trace);
                presentations.AddRange(execution.Presentations);
                operationEffects.AddRange(execution.OperationEffects);
                escalate |= execution.OperationEffects.OfType<EscalateHumanOperationEffect>().Any();
                completed |= execution.OperationEffects.OfType<CompleteRequestOperationEffect>().Any();
                domainEvents.AddRange(execution.DomainEvents);
                foreach (var sequence in execution.Sequences)
                    sequences.Add(sequence);
                foreach (var @event in execution.Events)
                    events.Add(@event);
                escalate |= execution.EscalateToHuman;
                completed |= execution.RequestCompleted;
                response = execution.Response ?? response;

                foreach (var verification in execution.OperationEffects.OfType<SaveVerificationEffect>())
                {
                    _verificationStore.Record(
                        request.OperationContext.ConversationState,
                        verification.VerificationType,
                        verification.Dependencies,
                        verification.Ttl);
                }
                verifications = ResolveActiveVerifications(request, currentFacts);
                if (execution.FactMutations.Count > 0)
                {
                    var effectBatch = _facts.ApplyMutations(schema, execution.FactMutations, currentFacts, versions);
                    await PersistFactsAsync(request, schema, effectBatch.Mutations, cancellationToken);
                    currentFacts = new Dictionary<string, string>(effectBatch.NextFacts, StringComparer.OrdinalIgnoreCase);
                    versions = new Dictionary<string, long>(effectBatch.Versions, StringComparer.OrdinalIgnoreCase);
                    changedFacts.UnionWith(effectBatch.ChangedFacts);
                }
            }

            var transitionContext = new DeterministicStageExecutionContext
            {
                OperationContext = CopyOperationContext(request.OperationContext, currentFacts),
                Facts = currentFacts,
                Signals = signals,
                LatestUserMessage = request.LatestUserMessage,
                ChangedFacts = changedFacts,
                ActiveVerifications = verifications,
                ExecutedActionKeys = request.ExecutedActionKeys
            };
            var transition = _transitions.Resolve(flow, selectedStage, transitionContext);
            if (!transition.ShouldTransition || string.IsNullOrWhiteSpace(transition.TargetStageId))
                break;

            ApplyTransitionSideEffects(transition.Effects, sequences, events, ref escalate, ref completed, presentations);
            var transitionFacts = TransitionFactMutations(transition.Effects);
            if (transitionFacts.Count > 0)
            {
                var transitionBatch = _facts.ApplyMutations(schema, transitionFacts, currentFacts, versions);
                await PersistFactsAsync(request, schema, transitionBatch.Mutations, cancellationToken);
                currentFacts = new Dictionary<string, string>(transitionBatch.NextFacts, StringComparer.OrdinalIgnoreCase);
                versions = new Dictionary<string, long>(transitionBatch.Versions, StringComparer.OrdinalIgnoreCase);
                changedFacts.UnionWith(transitionBatch.ChangedFacts);
            }

            selectedStage = flow.Stages.First(stage =>
                stage.Id.Equals(transition.TargetStageId, StringComparison.OrdinalIgnoreCase));
        }

        var resolvedResponse = response ?? selectedStage.Response;
        if (!string.IsNullOrWhiteSpace(resolvedResponse.SendMessageSequence))
            sequences.Add(resolvedResponse.SendMessageSequence);
        if (!string.IsNullOrWhiteSpace(resolvedResponse.Template))
        {
            presentations.Add(new OperationPresentation(
                resolvedResponse.Template,
                currentFacts.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase),
                MimosBabySpa.Application.Agents.Templates.FragmentRenderMode.Exclusive,
                MimosBabySpa.Application.Agents.Templates.FragmentPriority.Required));
        }
        return new DeterministicTurnResult
        {
            Success = true,
            Plan = proposal.Plan,
            PromptTokens = proposal.PromptTokens,
            CompletionTokens = proposal.CompletionTokens,
            Route = route,
            VisitedStages = visited,
            CurrentStageId = selectedStage.Id,
            Facts = currentFacts,
            FactVersions = versions,
            Trace = trace,
            Presentations = presentations,
            OperationEffects = operationEffects,
            Sequences = sequences.ToList(),
            Events = events.ToList(),
            DomainEvents = domainEvents,
            EscalateToHuman = escalate,
            RequestCompleted = completed,
            Response = resolvedResponse
        };
    }

    private HashSet<string> ResolveActiveVerifications(
        DeterministicTurnRequest request,
        IReadOnlyDictionary<string, string> facts)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var verificationType in request.OperationContext.ConversationState.Verifications.Keys.ToList())
        {
            if (_verificationStore.IsActive(
                    request.OperationContext.ConversationState,
                    verificationType,
                    facts))
                active.Add(verificationType);
            else
                active.Remove(verificationType);
        }
        return active;
    }
    private async Task PersistFactsAsync(
        DeterministicTurnRequest request,
        IReadOnlyDictionary<string, FactSchemaEntry> schema,
        IReadOnlyDictionary<string, string?> mutations,
        CancellationToken cancellationToken)
    {
        var remembered = mutations.Keys
            .Where(key => schema[key].ShouldRememberAcrossRequests())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _factStore.ApplyBatchAsync(
            request.OperationContext.ConversationId,
            request.OperationContext.BusinessId,
            mutations,
            remembered,
            cancellationToken);
    }

    private static AgentFlowStage? ResolvePlanningStage(DeterministicTurnRequest request)
    {
        var currentFlow = AgentFlowCatalog.Find(request.Config, request.CurrentFlowId);
        var currentStage = currentFlow?.Stages.FirstOrDefault(stage =>
            stage.Id.Equals(request.CurrentStageId, StringComparison.OrdinalIgnoreCase));
        return currentStage ?? AgentFlowCatalog.PrimaryFlow(request.Config)?.Stages.FirstOrDefault();
    }

    private static AgentFlowStage ResolveSelectedStage(
        DeterministicTurnRequest request,
        AgentFlowDefinition selectedFlow,
        FlowRouteDecision route)
    {
        if (selectedFlow.Id.Equals(request.CurrentFlowId, StringComparison.OrdinalIgnoreCase))
        {
            var current = selectedFlow.Stages.FirstOrDefault(stage =>
                stage.Id.Equals(request.CurrentStageId, StringComparison.OrdinalIgnoreCase));
            if (current is not null)
                return current;
        }
        return selectedFlow.Stages[0];
    }

    private static OperationContext CopyOperationContext(
        OperationContext source,
        IReadOnlyDictionary<string, string> facts) => new()
    {
        AgentId = source.AgentId,
        BusinessId = source.BusinessId,
        ConversationId = source.ConversationId,
        BusinessToday = source.BusinessToday,
        BusinessNow = source.BusinessNow,
        Config = source.Config,
        ConversationState = source.ConversationState,
        Facts = facts
    };

    private static Dictionary<string, string?> TransitionFactMutations(IReadOnlyList<StageEffectDefinition> effects)
    {
        var mutations = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var effect in effects)
        {
            if (effect.Type == StageEffectTypes.SetFact && !string.IsNullOrWhiteSpace(effect.Fact))
                mutations[effect.Fact] = ElementText(effect.Value);
            if (effect.Type == StageEffectTypes.ClearFacts)
                foreach (var fact in effect.Facts)
                    mutations[fact] = null;
        }
        return mutations;
    }

    private static void ApplyTransitionSideEffects(
        IReadOnlyList<StageEffectDefinition> effects,
        ISet<string> sequences,
        ISet<string> events,
        ref bool escalate,
        ref bool completed,
        ICollection<OperationPresentation> presentations)
    {
        foreach (var effect in effects)
        {
            if (effect.Type == StageEffectTypes.EnqueueSequence && !string.IsNullOrWhiteSpace(effect.Sequence))
                sequences.Add(effect.Sequence);
            if (effect.Type == StageEffectTypes.EmitEvent && !string.IsNullOrWhiteSpace(effect.Event))
                events.Add(effect.Event);
            if (effect.Type == StageEffectTypes.EscalateHuman)
                escalate = true;
            if (effect.Type == StageEffectTypes.CompleteRequest)
                completed = true;
            if (effect.Type == StageEffectTypes.AddPresentation && !string.IsNullOrWhiteSpace(effect.Template))
                presentations.Add(new OperationPresentation(effect.Template, new Dictionary<string, object?>()));
        }
    }

    private static string? ElementText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static DeterministicTurnResult Failure(
        string error,
        IReadOnlyDictionary<string, string> facts,
        TurnPlan? plan = null,
        FlowRouteDecision? route = null) => Failure([error], facts, plan, route);

    private static DeterministicTurnResult Failure(
        IReadOnlyList<string> errors,
        IReadOnlyDictionary<string, string> facts,
        TurnPlan? plan = null,
        FlowRouteDecision? route = null) => new()
    {
        Success = false,
        Errors = errors,
        Plan = plan,
        Route = route,
        Facts = facts
    };
}
