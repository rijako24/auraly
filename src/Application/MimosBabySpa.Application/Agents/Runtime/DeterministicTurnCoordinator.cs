using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Commerce;

using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Models;
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
    public IReadOnlyList<string> PlanningWarnings { get; init; } = [];
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
    private readonly IReadOnlyList<ITurnPlanningContextEnricher> _contextEnrichers;

    public DeterministicTurnCoordinator(
        ITurnPlanner planner,
        IDeterministicFlowSelector flows,
        FactMutationBatchProcessor facts,
        IConversationFactsService factStore,
        IConversationVerificationService verificationStore,
        DeterministicStageExecutor stages,
        DeterministicStageTransitionResolver transitions)
        : this(planner, flows, facts, factStore, verificationStore, stages, transitions, [])
    {
    }

    public DeterministicTurnCoordinator(
        ITurnPlanner planner,
        IDeterministicFlowSelector flows,
        FactMutationBatchProcessor facts,
        IConversationFactsService factStore,
        IConversationVerificationService verificationStore,
        DeterministicStageExecutor stages,
        DeterministicStageTransitionResolver transitions,
        IEnumerable<ITurnPlanningContextEnricher> contextEnrichers)
    {
        _planner = planner;
        _flows = flows;
        _facts = facts;
        _factStore = factStore;
        _verificationStore = verificationStore;
        _stages = stages;
        _transitions = transitions;
        _contextEnrichers = contextEnrichers.ToList();
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
        var structuredContext = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var enricher in _contextEnrichers)
        {
            var fragment = await enricher.EnrichAsync(
                request.Config,
                request.OperationContext,
                cancellationToken);
            if (fragment is not null && !structuredContext.TryAdd(fragment.Key, fragment.Value))
                return Failure($"Duplicate planning context key '{fragment.Key}'.", request.CurrentFacts);
        }

        var proposal = await _planner.PlanAsync(
            new TurnPlanningContext(
                request.Config,
                planningStage,
                scope,
                request.CurrentFacts,
                request.LatestUserMessage,
                request.OperationContext.BusinessNow,
                request.RecentConversation,
                structuredContext),
            cancellationToken);
        if (!proposal.Success || proposal.Plan is null)
            return Failure(proposal.Errors, request.CurrentFacts, proposal.Plan);

        var effectivePlan = ProtectPendingCommerceSelection(
            request.Config,
            request.CurrentFacts,
            request.LatestUserMessage,
            proposal.Plan,
            request.OperationContext.ConversationState.LastBotMessage);
        effectivePlan = ReconcileKnownFactAmbiguities(request.Config, effectivePlan, request.CurrentFacts);
        var turnExecutedActionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var state = request.OperationContext.ConversationState;
        var signature = ConfigurationSignature(request.Config);
        var pending = state.PendingTurnPlan;
        if (pending is not null
            && (pending.ExpiresAtUtc <= DateTime.UtcNow
                || !pending.ConfigurationSignature.Equals(signature, StringComparison.Ordinal)))
        {
            state.PendingTurnPlan = null;
            pending = null;
        }

        if (IsClarification(effectivePlan))
        {
            var switchesPendingFlow = pending is not null
                && !string.IsNullOrWhiteSpace(effectivePlan.FlowIntent.CandidateFlow)
                && !effectivePlan.FlowIntent.CandidateFlow.Equals(pending.FlowId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(effectivePlan.FlowIntent.Evidence);
            if (pending is not null
                && !switchesPendingFlow
                && TurnPlanParser.TryParse(pending.PlanJson, out var deferredClarification, out _)
                && deferredClarification is not null)
            {
                var ambiguousFields = pending.AmbiguousFields
                    .Concat(effectivePlan.Response.AmbiguousFields)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                effectivePlan = CombineDeferredPlans(deferredClarification, effectivePlan, ambiguousFields);
            }

            state.PendingTurnPlan = CreatePending(request, planningStage, effectivePlan, signature);
            return ClarificationRequired(request, planningStage, effectivePlan, effectivePlan.Response.AmbiguousFields, proposal);
        }

        if (pending is not null)
        {
            var switchesFlow = !string.IsNullOrWhiteSpace(effectivePlan.FlowIntent.CandidateFlow)
                && !effectivePlan.FlowIntent.CandidateFlow.Equals(pending.FlowId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(effectivePlan.FlowIntent.Evidence);
            if (switchesFlow)
            {
                state.PendingTurnPlan = null;
            }
            else if (!TurnPlanParser.TryParse(pending.PlanJson, out var deferredPlan, out _)
                     || deferredPlan is null)
            {
                state.PendingTurnPlan = null;
            }
            else if (!ResolvesPending(effectivePlan, pending.AmbiguousFields))
            {
                var resolvedFields = ResolvedPendingFields(effectivePlan, pending.AmbiguousFields);
                if (resolvedFields.Count > 0)
                {
                    var remainingFields = pending.AmbiguousFields
                        .Where(field => !resolvedFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    var updatedDeferred = MarkAmbiguous(
                        MergeDeferredPlan(deferredPlan, effectivePlan, resolvedFields),
                        remainingFields);
                    state.PendingTurnPlan = CreatePending(request, planningStage, updatedDeferred, signature);
                    return ClarificationRequired(request, planningStage, updatedDeferred, remainingFields, proposal);
                }

                if (!HasIndependentMeaning(effectivePlan, pending.AmbiguousFields))
                    return ClarificationRequired(request, planningStage, deferredPlan, pending.AmbiguousFields, proposal);

                effectivePlan = KeepIndependentMeaning(effectivePlan, pending.AmbiguousFields);
            }
            else
            {
                effectivePlan = MergeDeferredPlan(deferredPlan, effectivePlan, pending.AmbiguousFields);
                state.PendingTurnPlan = null;
            }
        }

        var route = _flows.Select(
            request.Config,
            effectivePlan,
            new FlowSelectionContext(request.ActiveFlowId, request.HasOpenPrimaryRequest));
        var flow = AgentFlowCatalog.Find(request.Config, route.ActiveFlowId);
        if (flow is null || flow.Stages.Count == 0)
            return Failure($"Selected flow '{route.ActiveFlowId}' has no stages.", request.CurrentFacts, effectivePlan, route);

        var schema = request.Config.FactSchema.ToDictionary(fact => fact.Key, StringComparer.OrdinalIgnoreCase);
        var factBatch = _facts.Apply(schema, effectivePlan.Facts, request.CurrentFacts, request.FactVersions);
        await PersistFactsAsync(request, schema, factBatch.Mutations, cancellationToken);

        var currentFacts = new Dictionary<string, string>(factBatch.NextFacts, StringComparer.OrdinalIgnoreCase);
        var versions = new Dictionary<string, long>(factBatch.Versions, StringComparer.OrdinalIgnoreCase);
        var changedFacts = new HashSet<string>(factBatch.ChangedFacts, StringComparer.OrdinalIgnoreCase);
        var reentryInvalidation = FlowCheckpointInvalidation.GetInvalidations(
            request.Config,
            changedFacts);
        if (reentryInvalidation.FactsToClear.Count > 0)
        {
            var reentryMutations = reentryInvalidation.FactsToClear.ToDictionary(
                key => key,
                _ => (string?)null,
                StringComparer.OrdinalIgnoreCase);
            var reentryBatch = _facts.ApplyMutations(schema, reentryMutations, currentFacts, versions);
            await PersistFactsAsync(request, schema, reentryBatch.Mutations, cancellationToken);
            currentFacts = new Dictionary<string, string>(reentryBatch.NextFacts, StringComparer.OrdinalIgnoreCase);
            versions = new Dictionary<string, long>(reentryBatch.Versions, StringComparer.OrdinalIgnoreCase);
            changedFacts.UnionWith(reentryBatch.ChangedFacts);
        }
        var verifications = ResolveActiveVerifications(request, currentFacts);
        var signals = TurnPlanRuntimeMapper.ToSemanticSignals(effectivePlan);
        var selectedStage = ResolveSelectedStage(
            request,
            flow,
            route,
            currentFacts,
            reentryInvalidation.FactsToClear.Count > 0);

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

        // A stage declaration is an explicit override of the global default for that signal.
        // This guarantees one deterministic owner per turn even when a capability has a
        // stage-specific presentation in some checkpoints and a global fallback elsewhere.
        var stageOwnedSignals = selectedStage.Signals
            .Select(signal => signal.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var globalAction in request.Config.GlobalActions
                     .Where(action => !stageOwnedSignals.Contains(action.Signal.Type)
                         && signals.Any(signal => signal.Type.Equals(
                             action.Signal.Type,
                             StringComparison.OrdinalIgnoreCase)))
                     .OrderByDescending(action => action.Priority))
        {
            var globalStage = new AgentFlowStage
            {
                Id = $"global:{globalAction.Id}",
                Actions = globalAction.Actions,
                Response = globalAction.Response
            };
            var globalContext = new DeterministicStageExecutionContext
            {
                OperationContext = CopyOperationContext(request.OperationContext, currentFacts),
                Facts = currentFacts,
                Signals = signals,
                LatestUserMessage = request.LatestUserMessage,
                ChangedFacts = changedFacts,
                ActiveVerifications = verifications,
                ExecutedActionKeys = request.ExecutedActionKeys,
                TurnExecutedActionKeys = turnExecutedActionKeys
            };
            var execution = await _stages.ExecuteAsync(
                globalStage,
                StageActionTriggers.OnSignal,
                globalContext,
                cancellationToken);
            trace.AddRange(execution.Trace);
            presentations.AddRange(execution.Presentations);
            operationEffects.AddRange(execution.OperationEffects);
            domainEvents.AddRange(execution.DomainEvents);
            foreach (var sequence in execution.Sequences)
                sequences.Add(sequence);
            foreach (var @event in execution.Events)
                events.Add(@event);
            escalate |= execution.EscalateToHuman
                || execution.OperationEffects.OfType<EscalateHumanOperationEffect>().Any();
            completed |= execution.RequestCompleted
                || execution.OperationEffects.OfType<CompleteRequestOperationEffect>().Any();
            response = execution.Response ?? response ?? globalAction.Response;

            if (execution.FactMutations.Count > 0)
            {
                var globalBatch = _facts.ApplyMutations(schema, execution.FactMutations, currentFacts, versions);
                await PersistFactsAsync(request, schema, globalBatch.Mutations, cancellationToken);
                currentFacts = new Dictionary<string, string>(globalBatch.NextFacts, StringComparer.OrdinalIgnoreCase);
                versions = new Dictionary<string, long>(globalBatch.Versions, StringComparer.OrdinalIgnoreCase);
                changedFacts.UnionWith(globalBatch.ChangedFacts);
            }

            var requestReset = execution.OperationEffects
                .OfType<ResetRequestOperationEffect>()
                .LastOrDefault();
            if (requestReset is not null)
            {
                foreach (var key in requestReset.ClearedFacts)
                {
                    currentFacts.Remove(key);
                    versions.Remove(key);
                    changedFacts.Add(key);
                }
                selectedStage = flow.Stages[0];
            }
        }

        for (var hop = 0; hop <= flow.Stages.Count; hop++)
        {
            if (!seenStages.Add(selectedStage.Id))
                return Failure($"Stage transition cycle detected at '{selectedStage.Id}'.", currentFacts, effectivePlan, route);
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
                    ExecutedActionKeys = request.ExecutedActionKeys,
                    TurnExecutedActionKeys = turnExecutedActionKeys
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
                ExecutedActionKeys = request.ExecutedActionKeys,
                TurnExecutedActionKeys = turnExecutedActionKeys
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
            Plan = effectivePlan,
            PlanningWarnings = proposal.Warnings,
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
        FlowRouteDecision route,
        IReadOnlyDictionary<string, string> currentFacts,
        bool resolveReentry)
    {
        if (selectedFlow.Id.Equals(request.CurrentFlowId, StringComparison.OrdinalIgnoreCase))
        {
            var current = selectedFlow.Stages.FirstOrDefault(stage =>
                stage.Id.Equals(request.CurrentStageId, StringComparison.OrdinalIgnoreCase));
            if (current is not null)
            {
                if (!resolveReentry)
                    return current;
                var position = new ConversationState
                {
                    ActiveStageId = current.Id,
                    ActiveFlowId = selectedFlow.Id
                };
                return DeterministicConversationPosition.ResolveStage(
                    selectedFlow, position, currentFacts, request.Config.FactSchema);
            }
        }
        return selectedFlow.Stages[0];
    }

    private static OperationContext CopyOperationContext(
        OperationContext source,
        IReadOnlyDictionary<string, string> facts)
    {
        if (source.Session is not null)
        {
            source.Session.Facts.Clear();
            foreach (var (key, value) in facts)
                source.Session.Facts[key] = value;
        }

        return new OperationContext
        {
            AgentId = source.AgentId,
            BusinessId = source.BusinessId,
            ConversationId = source.ConversationId,
            BusinessToday = source.BusinessToday,
            BusinessNow = source.BusinessNow,
            Config = source.Config,
            ConversationState = source.ConversationState,
            Facts = facts,
            Session = source.Session
        };
    }
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

    private static bool IsClarification(TurnPlan plan) =>
        plan.Response.Mode.Equals("ask_clarification", StringComparison.OrdinalIgnoreCase)
        && plan.Response.AmbiguousFields.Count > 0;

    internal static TurnPlan ReconcileKnownFactAmbiguities(
        AgentConfig config,
        TurnPlan plan,
        IReadOnlyDictionary<string, string> currentFacts)
    {
        if (plan.Response.AmbiguousFields.Count == 0)
            return plan;

        var knownFactKeys = config.FactSchema
            .Select(fact => fact.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var explicitlyCleared = plan.Facts
            .Where(fact => fact.Operation.Equals(TurnPlanOperations.Clear, StringComparison.OrdinalIgnoreCase))
            .Select(fact => fact.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unresolved = plan.Response.AmbiguousFields
            .Where(field => !knownFactKeys.Contains(field)
                || explicitlyCleared.Contains(field)
                || !currentFacts.TryGetValue(field, out var value)
                || string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unresolved.Count == plan.Response.AmbiguousFields.Count)
            return plan;

        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = plan.Facts,
            Signals = plan.Signals,
            Decision = plan.Decision,
            Response = new TurnPlanResponseDirective
            {
                Mode = unresolved.Count == 0 ? "continue" : "ask_clarification",
                AmbiguousFields = unresolved
            }
        };
    }

    private static PendingTurnPlan CreatePending(
        DeterministicTurnRequest request,
        AgentFlowStage stage,
        TurnPlan plan,
        string signature)
    {
        var now = DateTime.UtcNow;
        return new PendingTurnPlan
        {
            ConfigurationSignature = signature,
            FlowId = string.IsNullOrWhiteSpace(plan.FlowIntent.CandidateFlow)
                ? request.CurrentFlowId ?? string.Empty
                : plan.FlowIntent.CandidateFlow,
            StageId = stage.Id,
            PlanJson = JsonSerializer.Serialize(plan),
            AmbiguousFields = plan.Response.AmbiguousFields.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15)
        };
    }

    private static bool ResolvesPending(TurnPlan clarification, IReadOnlyList<string> fields) =>
        fields.All(field =>
            clarification.Facts.Any(fact => fact.Key.Equals(field, StringComparison.OrdinalIgnoreCase))
            || clarification.Signals.Any(signal => signal.Type.Equals(field, StringComparison.OrdinalIgnoreCase))
            || field.Equals("flowIntent", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(clarification.FlowIntent.Evidence)
            || field.Equals("decision", StringComparison.OrdinalIgnoreCase)
                && clarification.Decision is not null);

    internal static TurnPlan ProtectPendingCommerceSelection(
        AgentConfig config,
        IReadOnlyDictionary<string, string> currentFacts,
        string latestUserMessage,
        TurnPlan plan,
        string? lastBotMessage = null)
    {
        if (!config.Commerce.Enabled)
            return plan;
        plan = RecoverCartReviewIntent(config, latestUserMessage, plan);
        var pending = PendingCartCommandMemory.Read(currentFacts);
        if (pending is null)
            return plan;
        var contextualConfirmation = PendingCartCommandMemory.IsContextualConfirmation(
            latestUserMessage, config.Commerce.Conversation);

        var finalizationKeys = config.FactSchema
            .Where(fact => fact.Role?.Equals("order.finalized", StringComparison.OrdinalIgnoreCase) == true)
            .Select(fact => fact.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finalizationRequested = plan.Facts.Any(fact => finalizationKeys.Contains(fact.Key)
            && fact.Operation.Equals(TurnPlanOperations.Set, StringComparison.OrdinalIgnoreCase)
            && IsTrue(fact.Value))
            || CommerceConversationMatcher.Matches(
                latestUserMessage, config.Commerce.Conversation.FinalizationRules);
        var cartSignal = AgentFlowCatalog.EffectiveFlows(config)
            .SelectMany(flow => flow.Stages)
            .SelectMany(stage => stage.Actions)
            .FirstOrDefault(action =>
                action.Operation.Equals(ApplyOrderChangesOperation.OperationId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(action.Signal))
            ?.Signal;
        plan = RemoveUngroundedPendingReplays(
            plan, pending, latestUserMessage, cartSignal, config.Commerce.Matching);
        plan = RecoverPendingCartIntent(
            config, pending, latestUserMessage, lastBotMessage, plan, cartSignal);
        var discardOnFinalizeCodes = config.Commerce.PendingCart.DiscardOnFinalizeIssueCodes
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var discardAllOnExplicitFinalization = finalizationRequested
            && config.Commerce.PendingCart.DiscardAllOnExplicitFinalization;
        var discardableItems = pending.Items
            .Where(item => item.RequiresResolution
                && (discardAllOnExplicitFinalization
                    || item.Issue is not null
                    && discardOnFinalizeCodes.Contains(item.Issue.Code)))
            .ToList();
        var blockingItems = pending.Items
            .Where(item => item.RequiresResolution && !discardableItems.Contains(item))
            .ToList();
        var finalizeConfirmation = discardableItems.Count > 0
            && blockingItems.Count == 0
            && CommerceConversationMatcher.IsExactPhrase(
                latestUserMessage, config.Commerce.PendingCart.FinalizeConfirmationPhrases);
        var shouldFinalizeDiscardablePending = finalizationRequested || finalizeConfirmation;
        if (shouldFinalizeDiscardablePending && discardableItems.Count > 0 && blockingItems.Count == 0)
        {
            var finalizationSignals = plan.Signals
                .Where(signal => string.IsNullOrWhiteSpace(cartSignal)
                    || !signal.Type.Equals(cartSignal, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!string.IsNullOrWhiteSpace(cartSignal))
            {
                var commands = plan.Signals
                    .Where(signal => signal.Type.Equals(cartSignal, StringComparison.OrdinalIgnoreCase)
                        && signal.Value.ValueKind == JsonValueKind.Array)
                    .SelectMany(signal => signal.Value.EnumerateArray())
                    .Where(command => !command.TryGetProperty("operation", out var operation)
                        || operation.GetString()?.Equals(CartCommandOperations.CancelPending, StringComparison.OrdinalIgnoreCase) != true)
                    .Select(command => command.Clone())
                    .ToList();
                commands.AddRange(discardableItems.Select(item => JsonSerializer.SerializeToElement(new
                {
                    operation = CartCommandOperations.CancelPending,
                    productText = item.OriginalProductText,
                    quantity = (decimal?)null,
                    destinationReference = (string?)null
                })));
                finalizationSignals.Add(new PlannedSignal
                {
                    Type = cartSignal,
                    Value = JsonSerializer.SerializeToElement(commands),
                    Evidence = latestUserMessage,
                    Confidence = 1
                });
            }

            var finalizationFacts = plan.Facts.ToList();
            if (finalizationKeys.Count == 1 && !finalizationFacts.Any(fact => finalizationKeys.Contains(fact.Key)))
            {
                finalizationFacts.Add(new PlannedFactClaim
                {
                    Key = finalizationKeys.Single(),
                    Operation = TurnPlanOperations.Set,
                    Value = JsonSerializer.SerializeToElement(true),
                    Evidence = latestUserMessage,
                    Confidence = 1
                });
            }

            return new TurnPlan
            {
                FlowIntent = plan.FlowIntent,
                Facts = finalizationFacts,
                Signals = finalizationSignals,
                Decision = plan.Decision,
                Response = new TurnPlanResponseDirective { Mode = "continue", AmbiguousFields = [] }
            };
        }
        var emptyPlan = plan.Facts.Count == 0 && plan.Signals.Count == 0 && plan.Decision is null;
        if (emptyPlan && IsUnrelatedCatalogSelectionWithoutQuantity(
                config, currentFacts, pending, latestUserMessage))
            return plan;
        if (!finalizationRequested && !emptyPlan && !contextualConfirmation)
            return plan;


        var sourceSignals = contextualConfirmation
            ? plan.Signals.Where(signal => signal.Type.Equals(cartSignal, StringComparison.OrdinalIgnoreCase))
            : plan.Signals;
        var signals = sourceSignals.Select(signal =>
        {
            if ((!finalizationRequested && !contextualConfirmation)
                || string.IsNullOrWhiteSpace(cartSignal)
                || !signal.Type.Equals(cartSignal, StringComparison.OrdinalIgnoreCase)
                || signal.Value.ValueKind != JsonValueKind.Array)
                return signal;

            var safeCommands = signal.Value.EnumerateArray()
                .Where(command => !command.TryGetProperty("operation", out var operation)
                    || operation.GetString()?.Equals(CartCommandOperations.CancelPending, StringComparison.OrdinalIgnoreCase) != true)
                .Select(command => command.Clone())
                .ToList();
            return new PlannedSignal
            {
                Type = signal.Type,
                Value = JsonSerializer.SerializeToElement(safeCommands),
                Evidence = signal.Evidence,
                Confidence = signal.Confidence
            };
        }).ToList();
        if (!string.IsNullOrWhiteSpace(cartSignal)
            && !signals.Any(signal => signal.Type.Equals(cartSignal, StringComparison.OrdinalIgnoreCase)))
        {
            signals.Add(new PlannedSignal
            {
                Type = cartSignal,
                Value = JsonSerializer.SerializeToElement(Array.Empty<object>()),
                Evidence = latestUserMessage,
                Confidence = 1
            });
        }

        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = finalizationRequested
                ? plan.Facts.Where(fact => !finalizationKeys.Contains(fact.Key)).ToList()
                : plan.Facts,
            Signals = signals,
            Decision = plan.Decision,
            Response = new TurnPlanResponseDirective { Mode = "continue", AmbiguousFields = [] }
        };
    }

    private static bool IsUnrelatedCatalogSelectionWithoutQuantity(
        AgentConfig config,
        IReadOnlyDictionary<string, string> currentFacts,
        PendingCartCommandBatch pending,
        string latestUserMessage)
    {
        if (PendingCartCommandMemory.TryReadSingleQuantity(
                latestUserMessage,
                config.Commerce.Conversation,
                out _))
            return false;

        var context = new AgentConversationContext { Config = config };
        foreach (var (key, value) in currentFacts)
            context.Facts[key] = value;
        var selectedProducts = ProductSelectionMemory.FindCatalogMatches(
            context,
            latestUserMessage);
        if (selectedProducts.Count == 0)
            return false;

        var pendingCandidates = pending.Items
            .Where(item => item.RequiresResolution)
            .SelectMany(item => item.Issue?.ProductCandidates ?? [])
            .ToList();
        return !selectedProducts.Any(selected => pendingCandidates.Any(candidate =>
            SameProduct(candidate, selected)));
    }

    private static bool SameProduct(
        CartCommandCandidate candidate,
        ProductReference product)
    {
        if (candidate.ProductId.HasValue && product.ProductId.HasValue)
            return candidate.ProductId.Value == product.ProductId.Value;
        if (!string.IsNullOrWhiteSpace(candidate.ExternalProductId)
            && !string.IsNullOrWhiteSpace(product.ExternalProductId))
        {
            return candidate.ExternalProductId.Equals(
                product.ExternalProductId,
                StringComparison.OrdinalIgnoreCase);
        }
        if (!string.IsNullOrWhiteSpace(candidate.Sku)
            && !string.IsNullOrWhiteSpace(product.Sku))
            return candidate.Sku.Equals(product.Sku, StringComparison.OrdinalIgnoreCase);
        return candidate.Name.Equals(product.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static TurnPlan RecoverCartReviewIntent(
        AgentConfig config,
        string latestUserMessage,
        TurnPlan plan)
    {
        if (!CommerceConversationMatcher.Matches(
                latestUserMessage, config.Commerce.Conversation.CartReviewRules))
            return plan;

        var globalAction = config.GlobalActions.FirstOrDefault(candidate =>
            candidate.Actions.Any(action => action.Operation.Equals(
                GetOrderDraftOperation.OperationId, StringComparison.OrdinalIgnoreCase)));
        var signal = globalAction?.Signal.Type
            ?? AgentFlowCatalog.EffectiveFlows(config)
                .SelectMany(flow => flow.Stages)
                .SelectMany(stage => stage.Actions)
                .FirstOrDefault(action => action.Operation.Equals(
                    GetOrderDraftOperation.OperationId, StringComparison.OrdinalIgnoreCase))
                ?.Signal;
        if (string.IsNullOrWhiteSpace(signal))
            return plan;

        var finalizationKeys = config.FactSchema
            .Where(fact => fact.Role?.Equals("order.finalized", StringComparison.OrdinalIgnoreCase) == true)
            .Select(fact => fact.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = plan.Facts.Where(fact => !finalizationKeys.Contains(fact.Key)).ToList(),
            Signals =
            [
                new PlannedSignal
                {
                    Type = signal,
                    Value = JsonSerializer.SerializeToElement(new { }),
                    Evidence = latestUserMessage,
                    Confidence = 1
                }
            ],
            Decision = plan.Decision,
            Response = new TurnPlanResponseDirective { Mode = "continue", AmbiguousFields = [] }
        };
    }
    private static TurnPlan RecoverPendingCartIntent(
        AgentConfig config,
        PendingCartCommandBatch pending,
        string latestUserMessage,
        string? lastBotMessage,
        TurnPlan plan,
        string? cartSignal)
    {
        if (string.IsNullOrWhiteSpace(cartSignal)
            || plan.Signals.Any(signal =>
                signal.Type.Equals(cartSignal, StringComparison.OrdinalIgnoreCase)
                && signal.Value.ValueKind == JsonValueKind.Array
                && signal.Value.GetArrayLength() > 0))
            return plan;

        var unresolved = pending.Items.Where(item => item.RequiresResolution).ToList();
        CartCommand? recoveredCommand = null;
        if (CommerceConversationMatcher.Matches(
                latestUserMessage, config.Commerce.PendingCart.CancellationRules))
        {
            var target = PendingCartCommandMemory.FindReferencedItem(
                    unresolved, latestUserMessage, config.Commerce.Matching)
                ?? PendingCartCommandMemory.FindUniquelyReferencedItem(
                    unresolved, lastBotMessage, config.Commerce.Matching)
                ?? (unresolved.Count == 1 ? unresolved[0] : null);
            if (target is not null)
            {
                recoveredCommand = new CartCommand(
                    CartCommandOperations.CancelPending,
                    target.OriginalProductText,
                    null,
                    null);
            }
        }

        if (recoveredCommand is null
            && CommerceConversationMatcher.ContainsPhrase(
                latestUserMessage, config.Commerce.PendingCart.QuantityCorrectionPhrases)
            && PendingCartCommandMemory.TryReadSingleQuantity(
                latestUserMessage, config.Commerce.Conversation, out var quantity))
        {
            var insufficientStockItems = unresolved
                .Where(item => item.Issue?.Code.Equals(
                    "insufficient_stock", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            var target = PendingCartCommandMemory.FindReferencedItem(
                    insufficientStockItems, latestUserMessage, config.Commerce.Matching)
                ?? (insufficientStockItems.Count == 1 ? insufficientStockItems[0] : null);
            if (target is not null
                && (target.Issue?.MaximumCommandQuantity is not { } maximum
                    || quantity <= maximum))
            {
                var operation = target.Command.Operation is CartCommandOperations.Add
                    or CartCommandOperations.SetQuantity
                    ? target.Command.Operation
                    : CartCommandOperations.Add;
                recoveredCommand = new CartCommand(
                    operation,
                    target.OriginalProductText,
                    quantity,
                    target.Command.DestinationReference);
            }
        }

        if (recoveredCommand is null)
            return plan;

        var command = recoveredCommand;
        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = plan.Facts,
            Signals =
            [
                new PlannedSignal
                {
                    Type = cartSignal,
                    Value = JsonSerializer.SerializeToElement(new[]
                    {
                        new
                        {
                            operation = command.Operation,
                            productText = command.ProductText,
                            quantity = command.Quantity,
                            destinationReference = command.DestinationReference
                        }
                    }),
                    Evidence = latestUserMessage,
                    Confidence = 1
                }
            ],
            Decision = plan.Decision,
            Response = new TurnPlanResponseDirective { Mode = "continue", AmbiguousFields = [] }
        };
    }
    private static TurnPlan RemoveUngroundedPendingReplays(
        TurnPlan plan,
        PendingCartCommandBatch pending,
        string latestUserMessage,
        string? cartSignal,
        ProductMatchingPolicy matchingPolicy)
    {
        if (string.IsNullOrWhiteSpace(cartSignal))
            return plan;

        var changed = false;
        var signals = plan.Signals.Select(signal =>
        {
            if (!signal.Type.Equals(cartSignal, StringComparison.OrdinalIgnoreCase)
                || signal.Value.ValueKind != JsonValueKind.Array)
                return signal;

            var commands = signal.Value.EnumerateArray().ToList();
            var grounded = commands.Where(command =>
            {
                if (!command.TryGetProperty("operation", out var operation)
                    || operation.GetString() is not (CartCommandOperations.Add or CartCommandOperations.SetQuantity)
                    || !command.TryGetProperty("productText", out var productTextElement))
                    return true;
                var productText = productTextElement.GetString() ?? string.Empty;
                return !pending.Items.Any(item => item.RequiresResolution
                    && IsSamePendingReference(item, productText)
                    && !MessageReferencesPendingItem(latestUserMessage, item, matchingPolicy));
            }).Select(command => command.Clone()).ToList();
            if (grounded.Count == commands.Count)
                return signal;
            changed = true;
            return new PlannedSignal
            {
                Type = signal.Type,
                Value = JsonSerializer.SerializeToElement(grounded),
                Evidence = signal.Evidence,
                Confidence = signal.Confidence
            };
        }).ToList();

        if (!changed)
            return plan;
        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = plan.Facts,
            Signals = signals,
            Decision = plan.Decision,
            Response = plan.Response
        };
    }

    private static bool IsSamePendingReference(PendingCartItem item, string productText)
    {
        var normalized = CatalogSearchText.NormalizeCompact(productText);
        return normalized.Length > 0 && new[]
        {
            item.OriginalProductText,
            item.Command.ProductText,
            item.Issue?.ProductText ?? string.Empty
        }.Any(reference => normalized == CatalogSearchText.NormalizeCompact(reference));
    }

    private static bool MessageReferencesPendingItem(
        string message,
        PendingCartItem item,
        ProductMatchingPolicy matchingPolicy)
    {
        var messageTokens = ProductSearchText.GetMatchingTokens(message);
        if (messageTokens.Count == 0)
            return false;
        return new[]
        {
            item.OriginalProductText,
            item.Command.ProductText,
            item.Issue?.ProductText ?? string.Empty
        }.Any(reference =>
        {
            var referenceTokens = ProductSearchText.GetMatchingTokens(reference);
            return referenceTokens.Count > 0 && referenceTokens.All(token =>
                messageTokens.Any(messageToken => token == messageToken
                    || ProductSearchText.TokenSimilarity(token, messageToken)
                        >= matchingPolicy.PendingReferenceSimilarity));
        });
    }
    private static bool IsTrue(JsonElement value) =>
        value.ValueKind == JsonValueKind.True
        || value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out var parsed)
            && parsed;
    private static IReadOnlyList<string> ResolvedPendingFields(
        TurnPlan plan,
        IReadOnlyList<string> fields) =>
        fields.Where(field =>
                plan.Facts.Any(fact => fact.Key.Equals(field, StringComparison.OrdinalIgnoreCase))
                || plan.Signals.Any(signal => signal.Type.Equals(field, StringComparison.OrdinalIgnoreCase))
                || field.Equals("flowIntent", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(plan.FlowIntent.Evidence)
                || field.Equals("decision", StringComparison.OrdinalIgnoreCase)
                    && plan.Decision is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static TurnPlan CombineDeferredPlans(
        TurnPlan deferred,
        TurnPlan current,
        IReadOnlyList<string> ambiguousFields) =>
        new()
        {
            FlowIntent = !string.IsNullOrWhiteSpace(current.FlowIntent.Evidence)
                ? current.FlowIntent
                : deferred.FlowIntent,
            Facts = deferred.Facts
                .Concat(current.Facts)
                .GroupBy(fact => fact.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList(),
            Signals = deferred.Signals
                .Concat(current.Signals)
                .GroupBy(signal => signal.Type, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList(),
            Decision = current.Decision ?? deferred.Decision,
            Response = new TurnPlanResponseDirective
            {
                Mode = "ask_clarification",
                AmbiguousFields = ambiguousFields
            }
        };

    private static TurnPlan MarkAmbiguous(
        TurnPlan plan,
        IReadOnlyList<string> ambiguousFields) =>
        new()
        {
            FlowIntent = plan.FlowIntent,
            Facts = plan.Facts,
            Signals = plan.Signals,
            Decision = plan.Decision,
            Response = new TurnPlanResponseDirective
            {
                Mode = "ask_clarification",
                AmbiguousFields = ambiguousFields
            }
        };

    private static bool HasIndependentMeaning(
        TurnPlan plan,
        IReadOnlyList<string> pendingFields)
    {
        var pending = pendingFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return plan.Facts.Any(fact => !pending.Contains(fact.Key))
            || plan.Signals.Any(signal => !pending.Contains(signal.Type))
            || plan.Decision is not null && !pending.Contains("decision");
    }

    private static TurnPlan KeepIndependentMeaning(
        TurnPlan plan,
        IReadOnlyList<string> pendingFields)
    {
        var pending = pendingFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = plan.Facts.Where(fact => !pending.Contains(fact.Key)).ToList(),
            Signals = plan.Signals.Where(signal => !pending.Contains(signal.Type)).ToList(),
            Decision = pending.Contains("decision") ? null : plan.Decision,
            Response = new TurnPlanResponseDirective { Mode = "continue", AmbiguousFields = [] }
        };
    }

    private static TurnPlan MergeDeferredPlan(
        TurnPlan deferred,
        TurnPlan clarification,
        IReadOnlyList<string> ambiguousFields)
    {
        var ambiguous = ambiguousFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new TurnPlan
        {
            FlowIntent = ambiguous.Contains("flowIntent") ? clarification.FlowIntent : deferred.FlowIntent,
            Facts = deferred.Facts
                .Where(fact => !ambiguous.Contains(fact.Key))
                .Concat(clarification.Facts)
                .GroupBy(fact => fact.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList(),
            Signals = deferred.Signals
                .Where(signal => !ambiguous.Contains(signal.Type))
                .Concat(clarification.Signals)
                .GroupBy(signal => signal.Type, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList(),
            Decision = ambiguous.Contains("decision")
                ? clarification.Decision
                : clarification.Decision ?? deferred.Decision,
            Response = new TurnPlanResponseDirective { Mode = "continue", AmbiguousFields = [] }
        };
    }

    private static DeterministicTurnResult ClarificationRequired(
        DeterministicTurnRequest request,
        AgentFlowStage stage,
        TurnPlan plan,
        IReadOnlyList<string> ambiguousFields,
        TurnPlanProposal proposal) => new()
        {
            Success = true,
            Plan = plan,
            PlanningWarnings = proposal.Warnings,
            PromptTokens = proposal.PromptTokens,
            CompletionTokens = proposal.CompletionTokens,
            CurrentStageId = request.CurrentStageId,
            Facts = new Dictionary<string, string>(request.CurrentFacts, StringComparer.OrdinalIgnoreCase),
            FactVersions = new Dictionary<string, long>(request.FactVersions, StringComparer.OrdinalIgnoreCase),
            Response = new StageResponseDefinition
            {
                Mode = "ask_clarification",
                Guidance = $"Solicita la alternativa aplicable para: {string.Join(", ", ambiguousFields)}."
            }
        };

    private static string ConfigurationSignature(AgentConfig config)
    {
        var source = string.Join("|",
            config.AgentId,
            string.Join(",", config.FactSchema.Select(fact => $"{fact.Key}:{fact.ValueSource}")),
            string.Join(",", AgentFlowCatalog.EffectiveFlows(config).SelectMany(flow =>
                flow.Stages.SelectMany(stage =>
                    stage.Actions.Select(action => $"{flow.Id}:{stage.Id}:{action.Id}:{action.Operation}")))),
            string.Join(",", config.GlobalActions.SelectMany(global =>
                global.Actions.Select(action => $"{global.Id}:{action.Id}:{action.Operation}"))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

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
