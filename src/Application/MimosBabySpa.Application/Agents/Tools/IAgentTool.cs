using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;

namespace MimosBabySpa.Application.Agents.Tools;

/// <summary>
/// Contrato de una tool que el LLM puede invocar vía Function Calling.
/// </summary>
public interface IAgentTool
{
    string Name { get; }

    /// <summary>
    /// Pack al que pertenece. Null = tool transversal (ej. escalate_to_human).
    /// </summary>
    string? PackId => null;

    string Description { get; }

    string ParametersSchema { get; }

    IReadOnlyList<RoleRequirement> RoleRequirements => [];

    IReadOnlyList<string> RequiredTemplateIds => [];

    string? DefaultTemplateId => null;

    string? DefaultTemplate => null;

    IReadOnlyList<string> SemanticTriggers => [];

    ToolAvailabilityResult Evaluate(AgentToolContext ctx, JsonElement arguments) =>
        new(true, null, null);

    Func<JsonElement, AgentToolContext, string?>? VerificationScopeResolver => null;

    Task<string> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default);
}
