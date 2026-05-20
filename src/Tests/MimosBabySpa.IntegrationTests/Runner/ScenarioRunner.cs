using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.IntegrationTests.Bootstrap;
using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Interception;
using MimosBabySpa.IntegrationTests.Scenarios;
using MimosBabySpa.IntegrationTests.Validation;

namespace MimosBabySpa.IntegrationTests.Runner;

/// <summary>
/// Ejecuta una lista de TestScenarios secuencialmente, cada uno con su propio ServiceProvider aislado.
/// Recoge y devuelve instancias de ScenarioResult.
/// </summary>
public class ScenarioRunner
{
    private static readonly Guid BusinessId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AgentId    = new("22222222-2222-2222-2222-222222222222");
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

        try
        {
            // ── Construir FakeChatClient con todas las respuestas scripteadas ──
            var allLlmResults = scenario.Steps.SelectMany(s => s.LlmScript).ToList();
            var fakeChatClient = new FakeChatClient(allLlmResults);

            // ── DI container aislado por escenario ────────────────────────────
            var services = new ServiceCollection();
            TestServiceBuilder.Register(
                services,
                BusinessId,
                AgentId,
                scenario.CalendarMode,
                scenario.ReservationMode,
                toolLog,
                fakeChatClient);

            using var provider = services.BuildServiceProvider();
            var agentService   = provider.GetRequiredService<IAgentConversationService>();

            // ── Conversación única para el escenario ──────────────────────────
            var conversationId = Guid.NewGuid();

            // ── Ejecutar cada paso ────────────────────────────────────────────
            foreach (var (step, idx) in scenario.Steps.Select((s, i) => (s, i)))
            {
                var sw = Stopwatch.StartNew();
                string botResponse = "";
                string? stepError  = null;
                bool stepOk        = false;

                try
                {
                    var result = await agentService.ProcessMessageAsync(
                        AgentId,
                        conversationId,
                        step.UserMessage,
                        cancellationToken);

                    botResponse = result.Response;
                    stepOk      = result.Success;
                    if (!result.Success)
                        stepError = result.ErrorMessage;
                }
                catch (Exception ex)
                {
                    stepError   = ex.Message;
                    botResponse = $"[ERROR] {ex.Message}";
                }
                finally
                {
                    sw.Stop();
                }

                bool responseMatches = string.IsNullOrEmpty(step.ExpectedBotResponseContains)
                    || botResponse.Contains(step.ExpectedBotResponseContains, StringComparison.OrdinalIgnoreCase);

                stepResults.Add(new StepResult(
                    StepIndex:          idx,
                    UserMessage:        step.UserMessage,
                    BotResponse:        botResponse,
                    BotResponseMatches: responseMatches,
                    StepSucceeded:      stepOk,
                    ErrorMessage:       stepError,
                    ElapsedMs:          sw.ElapsedMilliseconds));
            }

            // ── Evaluar reglas de negocio ─────────────────────────────────────
            IReadOnlyList<TestRuleResult> ruleResults = scenario.RulesToValidate.Count == 0
                ? _ruleEngine.EvaluateAll(toolLog)
                : _ruleEngine.EvaluateNamed(toolLog, scenario.RulesToValidate);

            bool passed = stepResults.All(s => s.StepSucceeded && s.BotResponseMatches)
                       && ruleResults.All(r => r.Passed);

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
}
