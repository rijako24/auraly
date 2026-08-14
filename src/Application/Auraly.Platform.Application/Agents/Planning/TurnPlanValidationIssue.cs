namespace Auraly.Platform.Application.Agents.Planning;

public enum TurnPlanIssueTarget
{
    Plan,
    FlowIntent,
    Fact,
    Signal,
    Decision,
    Response
}

public enum TurnPlanRecoveryAction
{
    None,
    DropTarget,
    ClarifyTarget,
    FallbackToPrimaryFlow
}

public sealed record TurnPlanValidationIssue(
    string Code,
    string Message,
    TurnPlanIssueTarget Target,
    string? Field = null,
    TurnPlanRecoveryAction RecoveryAction = TurnPlanRecoveryAction.None);

public sealed record TurnPlanValidationResult(IReadOnlyList<TurnPlanValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
    public IReadOnlyList<string> Errors => Issues.Select(issue => issue.Message).ToList();
}
