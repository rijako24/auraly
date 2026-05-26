using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Packs.Booking;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Configuración de un agente cargada desde BD por turno (con caché).
/// El motor FlowEngine es el único orquestador; no hay modo legacy.
/// </summary>
public sealed class AgentConfig
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<PromptSection> PromptSections { get; init; } = [];

    public AgentFlowDefinition Flow { get; init; } = new();

    public IReadOnlyList<FactSchemaEntry> FactSchema { get; init; } = [];

    public IReadOnlyDictionary<string, GuardDefinition> Guards { get; init; }
        = new Dictionary<string, GuardDefinition>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Templates { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string SystemPrompt { get; init; } = string.Empty;

    public string BasePrompt
    {
        get
        {
            if (PromptSections.Count > 0)
            {
                return string.Join(
                    Environment.NewLine + Environment.NewLine,
                    PromptSections
                        .OrderBy(s => s.Order)
                        .Select(s => s.Content.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            return SystemPrompt.Trim();
        }
    }

    public IReadOnlyList<string> KillSwitchPhrases { get; init; } = [];

    public AgentHumanMessages HumanMessages { get; init; } = new();

    public AgentOperationalLimits OperationalLimits { get; init; } = new();

    public IReadOnlyList<string> CapabilityPacks { get; init; } = [BookingPackIds.Booking];

    public string Model { get; init; } = string.Empty;

    public float Temperature { get; init; } = 0.7f;

    public int MaxToolIterations { get; init; } = 6;

    public IReadOnlyList<string> EnabledToolNames { get; init; } = [];

    public int ConsecutiveErrorEscalationThreshold { get; init; } = 3;

    public IReadOnlyList<string> EscalationContacts { get; init; } = [];
}
