using MimosBabySpa.IntegrationTests.Interception;
using MimosBabySpa.IntegrationTests.Validation.Rules;

namespace MimosBabySpa.IntegrationTests.Validation;

/// <summary>
/// Runs all registered ITestRule implementations against a ToolCallLog.
/// </summary>
public class TestRuleEngine
{
    private readonly IReadOnlyList<ITestRule> _rules;

    public TestRuleEngine()
    {
        _rules = new List<ITestRule>
        {
            new ReservationMustCallCreateReservationRule(),
            new CheckAvailabilityBeforeCreateReservationRule(),
            new NoConfirmationWithoutAvailabilityCheckRule(),
            new BotMustNotInventTimeSlotsRule(),
            new NoDuplicateReservationRule(),
            new ReservationMustIncludeAddOns()
        };
    }

    public IReadOnlyList<TestRuleResult> EvaluateAll(ToolCallLog log) =>
        _rules.Select(r => r.Evaluate(log)).ToList();

    public IReadOnlyList<TestRuleResult> EvaluateNamed(ToolCallLog log, IEnumerable<string> ruleNames)
    {
        var set = new HashSet<string>(ruleNames, StringComparer.OrdinalIgnoreCase);
        return _rules.Where(r => set.Contains(r.Name))
                     .Select(r => r.Evaluate(log))
                     .ToList();
    }
}
