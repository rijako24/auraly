using MimosBabySpa.IntegrationTests.Runner;

namespace MimosBabySpa.IntegrationTests.Reports;

public class ConsoleReportPrinter
{
    public void Print(IReadOnlyList<ScenarioResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       MIMOS BABY SPA — INTEGRATION TEST REPORT             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"  Generado: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();

        foreach (var r in results)
        {
            var icon = r.Passed ? "✅ PASSED" : "❌ FAILED";
            Console.WriteLine($"┌─ [{icon}] {r.ScenarioId}");
            Console.WriteLine($"│  {r.ScenarioDescription}");
            Console.WriteLine($"│  ⏱  {r.TotalElapsedMs}ms | Pasos: {r.PassedSteps}/{r.TotalSteps} | Reglas: {r.PassedRules}/{r.RuleResults.Count}");

            if (r.ErrorMessage != null)
                Console.WriteLine($"│  💥 Error: {r.ErrorMessage}");

            foreach (var step in r.StepResults)
            {
                var stepIcon = step.StepSucceeded ? "  →" : "  💥";
                Console.WriteLine($"{stepIcon} [Paso {step.StepIndex + 1}] Usuario: \"{Truncate(step.UserMessage, 60)}\"");
                Console.WriteLine($"     Bot: \"{Truncate(step.BotResponse, 80)}\"");
                if (step.ErrorMessage != null)
                    Console.WriteLine($"     ⚠️ Error: {step.ErrorMessage}");
            }

            Console.WriteLine("│  REGLAS:");
            foreach (var rule in r.RuleResults)
                Console.WriteLine($"│    {rule.Message}");

            Console.WriteLine("└──────────────────────────────────────────────────────────────");
            Console.WriteLine();
        }

        // ── Summary ──────────────────────────────────────────────────────
        var total   = results.Count;
        var passed  = results.Count(r => r.Passed);
        var failed  = total - passed;
        var elapsed = results.Sum(r => r.TotalElapsedMs);

        Console.WriteLine("══════════════════ RESUMEN FINAL ══════════════════");
        Console.WriteLine($"  Total Escenarios : {total}");
        Console.WriteLine($"  ✅ Exitosos       : {passed}");
        Console.WriteLine($"  ❌ Fallidos       : {failed}");
        Console.WriteLine($"  ⏱  Tiempo total  : {elapsed}ms");
        Console.WriteLine($"  Resultado global  : {(failed == 0 ? "✅ ALL PASSED" : $"❌ {failed} FAILED")}");
        Console.WriteLine("════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
