namespace MimosBabySpa.Application.Agents.Packs.Leadgen;

public static class LeadgenPackIds
{
    public const string Leadgen = "leadgen";
}

public sealed class LeadgenPack : IToolCapabilityPack
{
    public string PackId => LeadgenPackIds.Leadgen;

    public IReadOnlyList<string> ToolNames { get; } =
    [
        "set_fact",
        "capture_lead",
        "escalate_to_human"
    ];

    public IReadOnlyDictionary<string, string> DefaultTemplates { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
