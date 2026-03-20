using System.Text.Json;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.Identity;

/// <summary>
/// Default flow document for newly created agents (start → end).
/// </summary>
public static class AgentFlowDefaults
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string BuildMinimalDefinitionJson()
    {
        var doc = new FlowDefinitionDocument
        {
            SessionConfig = new FlowSessionConfig(),
            EngineSettings = new FlowEngineSettings(),
            Nodes =
            [
                new FlowNode
                {
                    Id = "start",
                    Type = FlowNodeType.Start,
                    Label = "Inicio",
                    Config = JsonSerializer.Deserialize<JsonElement>("{}", JsonOpts)!
                },
                new FlowNode
                {
                    Id = "end",
                    Type = FlowNodeType.End,
                    Label = "Fin",
                    Config = JsonSerializer.Deserialize<JsonElement>("{}", JsonOpts)!
                }
            ],
            Edges =
            [
                new FlowEdge
                {
                    Id = "e-start-end",
                    SourceNodeId = "start",
                    TargetNodeId = "end",
                    PortId = null
                }
            ]
        };

        return JsonSerializer.Serialize(doc, JsonOpts);
    }
}
