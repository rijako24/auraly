using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;

namespace Auraly.Platform.Console;

public static class LeadQualificationConsoleScenario
{
    public static Task<int> RunAsync()
    {
        var config = new AgentConfig
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            Name = "Inmobiliaria smoke",
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "property_lead",
                    Type = FlowTypes.Primary,
                    Stages =
                    [
                        new AgentFlowStage { Id = "discovery", Goal = "Descubrir", LeadQualification = new() { Band = "exploring", Priority = 15, Label = "Búsqueda inicial" } },
                        new AgentFlowStage { Id = "visit", Goal = "Visita", LeadQualification = new() { Band = "high_intent", Priority = 80, Label = "Quiere visita" } },
                        new AgentFlowStage { Id = "handoff", Goal = "Entrega", LeadQualification = new() { Band = "sales_ready", Priority = 100, Label = "Lista para atención", ConversionOnRequestCompleted = true } }
                    ]
                }
            ]
        };

        var cases = new[]
        {
            ("exploración", "discovery", false, "exploring", 15, false),
            ("visita", "visit", false, "high_intent", 80, false),
            ("entrega sin completar", "handoff", false, "sales_ready", 100, false),
            ("entrega completada", "handoff", true, "sales_ready", 100, true)
        };

        var failures = 0;
        foreach (var test in cases)
        {
            var actual = LeadQualificationResolver.Resolve(config, "property_lead", test.Item2, test.Item3);
            var passed = actual is not null
                && actual.Band == test.Item4
                && actual.Priority == test.Item5
                && actual.Converted == test.Item6;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {test.Item1}");
            if (!passed) failures++;
        }

        var disabled = LeadQualificationResolver.Resolve(config, "property_lead", "unknown", false) is null;
        System.Console.WriteLine($"[{(disabled ? "PASS" : "FAIL")}] etapa sin configuración no altera lead");
        if (!disabled) failures++;

        System.Console.WriteLine($"[lead-qualification-smoke] total={cases.Length + 1} passed={cases.Length + 1 - failures} failed={failures}");
        return Task.FromResult(failures == 0 ? 0 : 1);
    }
}
