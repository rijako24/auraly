using MimosBabySpa.IntegrationTests.Validation;

namespace MimosBabySpa.IntegrationTests.Runner;

public record StepResult(
    int StepIndex,
    string UserMessage,
    string BotResponse,
    bool BotResponseMatches,
    bool StepSucceeded,
    string? ErrorMessage,
    long ElapsedMs);

public record ScenarioResult(
    string ScenarioId,
    string ScenarioDescription,
    bool Passed,
    IReadOnlyList<StepResult> StepResults,
    IReadOnlyList<TestRuleResult> RuleResults,
    string? ErrorMessage,
    long TotalElapsedMs,
    DateTimeOffset ExecutedAt)
{
    public int PassedRules => RuleResults.Count(r => r.Passed);
    public int FailedRules => RuleResults.Count(r => !r.Passed);
    public int TotalSteps  => StepResults.Count;
    public int PassedSteps => StepResults.Count(s => s.StepSucceeded);
}
