using System.Text.Json;
using System.Text.Json.Serialization;
using MimosBabySpa.IntegrationTests.Runner;

namespace MimosBabySpa.IntegrationTests.Reports;

public class JsonReportGenerator
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented        = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Generate(IReadOnlyList<ScenarioResult> results)
    {
        var report = new
        {
            GeneratedAt      = DateTimeOffset.UtcNow,
            TotalScenarios   = results.Count,
            PassedScenarios  = results.Count(r => r.Passed),
            FailedScenarios  = results.Count(r => !r.Passed),
            TotalElapsedMs   = results.Sum(r => r.TotalElapsedMs),
            Scenarios        = results.Select(MapScenario).ToList()
        };
        return JsonSerializer.Serialize(report, Options);
    }

    public async Task SaveAsync(IReadOnlyList<ScenarioResult> results, string outputPath)
    {
        var json = Generate(results);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, json);
    }

    private static object MapScenario(ScenarioResult r) => new
    {
        Id          = r.ScenarioId,
        Description = r.ScenarioDescription,
        r.Passed,
        r.TotalElapsedMs,
        r.ExecutedAt,
        r.ErrorMessage,
        Steps = r.StepResults.Select(s => new
        {
            s.StepIndex,
            s.UserMessage,
            s.BotResponse,
            s.BotResponseMatches,
            s.StepSucceeded,
            s.ErrorMessage,
            s.ElapsedMs
        }),
        Rules = r.RuleResults.Select(rule => new
        {
            rule.RuleName,
            rule.Passed,
            rule.Message
        })
    };
}
