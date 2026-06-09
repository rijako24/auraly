using MimosBabySpa.Application.LLM;
using MimosBabySpa.IntegrationTests.Infrastructure;

namespace MimosBabySpa.IntegrationTests.Scenarios;

/// <summary>
/// Representa un paso de conversación: mensaje del usuario y qué hace el "LLM falso" en ese turno.
/// LlmScript es la lista ordenada de ChatCompletionResult que el FakeChatClient devolverá
/// (una por llamada dentro del bucle de function calling).
/// </summary>
public record ConversationStep(
    string UserMessage,
    IReadOnlyList<ChatCompletionResult> LlmScript,
    string ExpectedBotResponseContains = "");

/// <summary>
/// Define un escenario de test completo: mocks, pasos de conversación y reglas a validar.
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
