using MimosBabySpa.IntegrationTests.Runner;

namespace MimosBabySpa.IntegrationTests.Reports;

public class ConsoleReportPrinter
{
    public void Print(IReadOnlyList<ScenarioResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("================================================================");
        Console.WriteLine("|       MIMOS BABY SPA - INTEGRATION TEST REPORT               |");
        Console.WriteLine("================================================================");
        Console.WriteLine($"  Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();

        foreach (var r in results)
        {
            var status = r.Passed ? "PASSED" : "FAILED";
            Console.WriteLine($"+- [{status}] {r.ScenarioId}");
            Console.WriteLine($"|  {r.ScenarioDescription}");
            Console.WriteLine($"|  Time: {r.TotalElapsedMs}ms | Steps: {r.PassedSteps}/{r.TotalSteps} | Rules: {r.PassedRules}/{r.RuleResults.Count}");

            if (r.ErrorMessage != null)
                Console.WriteLine($"|  Error: {r.ErrorMessage}");

            foreach (var step in r.StepResults)
            {
                var stepStatus = step.StepSucceeded ? "OK" : "ERROR";
                Console.WriteLine($"   {stepStatus} [Step {step.StepIndex + 1}] User: \"{Truncate(step.UserMessage, 60)}\"");
                Console.WriteLine($"      Bot: \"{Truncate(step.BotResponse, 80)}\"");
                if (step.ErrorMessage != null)
                    Console.WriteLine($"      Error: {step.ErrorMessage}");
            }

            Console.WriteLine("|  RULES:");
            foreach (var rule in r.RuleResults)
                Console.WriteLine($"|    {rule.Message}");

            Console.WriteLine("+--------------------------------------------------------------");
            Console.WriteLine();
        }

        // -- Summary --------------------------------------------------------
        var total   = results.Count;
        var passed  = results.Count(r => r.Passed);
        var failed  = total - passed;
        var elapsed = results.Sum(r => r.TotalElapsedMs);

        Console.WriteLine("================== FINAL SUMMARY ==================");
        Console.WriteLine($"  Total scenarios : {total}");
        Console.WriteLine($"  Passed          : {passed}");
        Console.WriteLine($"  Failed          : {failed}");
        Console.WriteLine($"  Total time      : {elapsed}ms");
        Console.WriteLine($"  Overall result  : {(failed == 0 ? "ALL PASSED" : $"{failed} FAILED")}");
        Console.WriteLine("===================================================");
        Console.WriteLine();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
