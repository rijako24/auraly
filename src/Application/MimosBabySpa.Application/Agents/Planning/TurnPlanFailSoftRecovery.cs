namespace MimosBabySpa.Application.Agents.Planning;

public static class TurnPlanFailSoftRecovery
{
    public static bool TryRecover(
        TurnPlan plan,
        TurnPlanValidationResult validation,
        TurnPlanScope scope,
        out TurnPlan recovered)
    {
        recovered = plan;
        if (validation.IsValid
            || validation.Issues.Any(issue => issue.RecoveryAction == TurnPlanRecoveryAction.None))
            return false;

        var droppedFacts = validation.Issues
            .Where(issue => issue.Target == TurnPlanIssueTarget.Fact
                && issue.RecoveryAction == TurnPlanRecoveryAction.DropTarget
                && !string.IsNullOrWhiteSpace(issue.Field))
            .Select(issue => issue.Field!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var droppedSignals = validation.Issues
            .Where(issue => issue.Target == TurnPlanIssueTarget.Signal
                && issue.RecoveryAction == TurnPlanRecoveryAction.DropTarget
                && !string.IsNullOrWhiteSpace(issue.Field))
            .Select(issue => issue.Field!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clarifyFields = validation.Issues
            .Where(issue => issue.RecoveryAction == TurnPlanRecoveryAction.ClarifyTarget
                && !string.IsNullOrWhiteSpace(issue.Field))
            .Select(issue => issue.Field!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropDecision = validation.Issues.Any(issue =>
            issue.Target == TurnPlanIssueTarget.Decision
            && issue.RecoveryAction == TurnPlanRecoveryAction.DropTarget);
        var fallbackFlow = validation.Issues.Any(issue =>
            issue.RecoveryAction == TurnPlanRecoveryAction.FallbackToPrimaryFlow);

        if (clarifyFields.Any(field => !scope.Facts.ContainsKey(field) && !scope.Signals.ContainsKey(field)))
            return false;
        if (fallbackFlow
            && (string.IsNullOrWhiteSpace(scope.PrimaryFlowId)
                || !scope.Flows.ContainsKey(scope.PrimaryFlowId)))
            return false;

        var ambiguous = plan.Response.AmbiguousFields
            .Concat(clarifyFields)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        recovered = new TurnPlan
        {
            FlowIntent = fallbackFlow
                ? new PlannedFlowIntent
                {
                    CandidateFlow = scope.PrimaryFlowId,
                    Confidence = 0,
                    Evidence = null
                }
                : plan.FlowIntent,
            Facts = plan.Facts.Where(fact => !droppedFacts.Contains(fact.Key)).ToList(),
            Signals = plan.Signals.Where(signal => !droppedSignals.Contains(signal.Type)).ToList(),
            Decision = dropDecision ? null : plan.Decision,
            Response = new TurnPlanResponseDirective
            {
                Mode = ambiguous.Count > 0 ? "ask_clarification" : plan.Response.Mode,
                AmbiguousFields = ambiguous
            }
        };
        return true;
    }
}