using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// Decide si un stage.lookup puede ejecutarse con los facts actuales del turno.
/// </summary>
internal static class FlowLookupGate
{
    public static bool CanExecute(
        AgentFlowStage stage,
        AgentToolContext session,
        IAgentTool? lookupTool)
    {
        if (stage.Lookup is null)
            return false;

        if (lookupTool is null)
            return true;

        var (_, unresolved) = FlowRefResolver.ResolveArgsDetailed(
            stage.Lookup.Args, session, null);

        if (unresolved.Count == 0)
            return true;

        foreach (var req in lookupTool.RoleRequirements)
        {
            if (!req.Required)
                continue;

            if (unresolved.Contains(req.ArgName, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static string FormatOmittedHint(IReadOnlyList<string> unresolvedKeys) =>
        unresolvedKeys.Count == 0
            ? string.Empty
            : $"Faltan datos para consultar disponibilidad/catálogo: {string.Join(", ", unresolvedKeys)}. "
              + "Pídelos al cliente antes de continuar.";
}
