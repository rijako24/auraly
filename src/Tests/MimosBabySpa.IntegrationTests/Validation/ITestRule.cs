using MimosBabySpa.IntegrationTests.Interception;

namespace MimosBabySpa.IntegrationTests.Validation;

/// <summary>
/// Contract for a test business-rule validator.
/// </summary>
public interface ITestRule
{
    string Name { get; }
    TestRuleResult Evaluate(ToolCallLog log);
}

/// <summary>
/// Result of evaluating a single test rule.
/// </summary>
public record TestRuleResult(bool Passed, string Message, string RuleName);
