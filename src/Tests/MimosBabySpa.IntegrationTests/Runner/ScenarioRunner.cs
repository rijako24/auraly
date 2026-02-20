using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.IntegrationTests.Bootstrap;
using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Interception;
using MimosBabySpa.IntegrationTests.Scenarios;
using MimosBabySpa.IntegrationTests.Validation;

namespace MimosBabySpa.IntegrationTests.Runner;

/// <summary>
/// Runs a list of TestScenarios sequentially, each with its own isolated ServiceProvider.
/// Collects and returns ScenarioResult instances.
/// </summary>
public class ScenarioRunner
{
    private static readonly Guid BusinessId = new("11111111-1111-1111-1111-111111111111");
    private readonly TestRuleEngine _ruleEngine = new();

    public async Task<IReadOnlyList<ScenarioResult>> RunAllAsync(
        IReadOnlyList<TestScenario> scenarios,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ScenarioResult>();
        foreach (var scenario in scenarios)
        {
            var result = await RunScenarioAsync(scenario, cancellationToken);
            results.Add(result);
        }
        return results;
    }

    public async Task<ScenarioResult> RunScenarioAsync(
        TestScenario scenario,
        CancellationToken cancellationToken = default)
    {
        var total = Stopwatch.StartNew();
        var stepResults = new List<StepResult>();
        var toolLog     = new ToolCallLog();
        string? scenarioError = null;
        bool passed = false;

        try
        {
            // ── Build scripts from steps ───────────────────────────────
            var scripts = scenario.Steps
                .Select((s, i) => new TurnScript(
                    ExtractionJson:          s.ExtractionJson,
                    ConversationalResponse:  GetConversationalResponse(s, i, scenario)))
                .ToList();

            // ── Build isolated DI container ────────────────────────────
            var services = new ServiceCollection();
            TestServiceBuilder.Register(
                services,
                BusinessId,
                scenario.CalendarMode,
                scenario.ReservationMode,
                toolLog,
                scripts);

            using var provider = services.BuildServiceProvider();
            var orchestrator   = provider.GetRequiredService<HybridTransactionalOrchestrator>();

            // ── Create a persisted conversation ────────────────────────
            var conversationId = Guid.NewGuid();
            var customerPhone  = "+5491100000000";

            // ── Execute each step ──────────────────────────────────────
            foreach (var (step, idx) in scenario.Steps.Select((s, i) => (s, i)))
            {
                var sw = Stopwatch.StartNew();
                string botResponse = "";
                string? stepError  = null;
                bool stepOk        = false;

                try
                {
                    var result = await orchestrator.ProcessMessageAsync(
                        conversationId,
                        BusinessId,
                        customerPhone,
                        step.UserMessage,
                        cancellationToken);

                    botResponse = result.Response;
                    stepOk      = true;
                }
                catch (Exception ex)
                {
                    stepError = ex.Message;
                    botResponse = $"[ERROR] {ex.Message}";
                }
                finally
                {
                    sw.Stop();
                }

                bool responseMatches = string.IsNullOrEmpty(step.ExpectedBotResponseContains)
                    || botResponse.Contains(step.ExpectedBotResponseContains, StringComparison.OrdinalIgnoreCase);

                stepResults.Add(new StepResult(
                    StepIndex:            idx,
                    UserMessage:          step.UserMessage,
                    BotResponse:          botResponse,
                    BotResponseMatches:   responseMatches,
                    StepSucceeded:        stepOk,
                    ErrorMessage:         stepError,
                    ElapsedMs:            sw.ElapsedMilliseconds));
            }

            // ── Evaluate business rules ────────────────────────────────
            IReadOnlyList<TestRuleResult> ruleResults = scenario.RulesToValidate.Count == 0
                ? _ruleEngine.EvaluateAll(toolLog)
                : _ruleEngine.EvaluateNamed(toolLog, scenario.RulesToValidate);

            passed = stepResults.All(s => s.StepSucceeded) && ruleResults.All(r => r.Passed);

            total.Stop();
            return new ScenarioResult(
                ScenarioId:          scenario.Id,
                ScenarioDescription: scenario.Description,
                Passed:              passed,
                StepResults:         stepResults,
                RuleResults:         ruleResults,
                ErrorMessage:        scenarioError,
                TotalElapsedMs:      total.ElapsedMilliseconds,
                ExecutedAt:          DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            total.Stop();
            return new ScenarioResult(
                ScenarioId:          scenario.Id,
                ScenarioDescription: scenario.Description,
                Passed:              false,
                StepResults:         stepResults,
                RuleResults:         [],
                ErrorMessage:        ex.Message,
                TotalElapsedMs:      total.ElapsedMilliseconds,
                ExecutedAt:          DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Maps a scenario step to the scripted bot conversational response.
    /// Generates a plausible bot reply based on the extraction intentions so
    /// the response contains the expected keywords (e.g. "disponib", "reserva").
    /// </summary>
    private static string GetConversationalResponse(
        ConversationStep step, int idx, TestScenario scenario)
    {
        // Try to detect what the step is doing from the JSON
        var json = step.ExtractionJson;
        bool confirming    = json.Contains("\"user_confirmed_booking\": true");
        bool checking      = json.Contains("\"user_requested_availability\": true");
        bool hasService    = json.Contains("\"Service\"") || json.Contains("\"Plan ");
        bool hasDate       = json.Contains("\"DesiredDate\"");

        if (confirming && !string.IsNullOrEmpty(step.ExpectedBotResponseContains)
                       && step.ExpectedBotResponseContains.Contains("reserva"))
            return "¡Perfecto! Tu reserva ha sido confirmada exitosamente. Te esperamos en Mimos Baby Spa.";

        if (confirming)
            return "Entendido. Voy a confirmar tu reserva ahora.";

        if (checking && hasService && hasDate)
            return "Déjame verificar la disponibilidad para esa fecha. " +
                   "Tenemos horarios disponibles: 10:00, 11:00 y 14:00. ¿Cuál prefiere?";

        if (checking)
            return "Verificando disponibilidad. Un momento por favor.";

        return "Claro, cuéntame más detalles para poder ayudarte mejor.";
    }
}
