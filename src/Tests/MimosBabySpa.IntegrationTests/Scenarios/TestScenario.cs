using MimosBabySpa.IntegrationTests.Infrastructure;

namespace MimosBabySpa.IntegrationTests.Scenarios;

/// <summary>Represents a single user message and the LLM scripteado turn.</summary>
public record ConversationStep(
    string UserMessage,
    string ExtractionJson,
    string ExpectedBotResponseContains = "");

/// <summary>
/// Defines a complete test scenario: which mocks to use, which steps to run,
/// and which business rules to validate at the end.
/// </summary>
public abstract class TestScenario
{
    public abstract string Id { get; }
    public abstract string Description { get; }
    public abstract IReadOnlyList<ConversationStep> Steps { get; }
    public virtual IReadOnlyList<string> RulesToValidate => [];
    public virtual CalendarMode CalendarMode => CalendarMode.Available;
    public virtual ReservationMode ReservationMode => ReservationMode.AlwaysSucceed;
    public virtual bool ExpectReservationCreated => false;
    public virtual bool ExpectAvailabilityChecked => false;
}
