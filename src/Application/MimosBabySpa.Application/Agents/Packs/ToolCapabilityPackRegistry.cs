namespace MimosBabySpa.Application.Agents.Packs;

public sealed class ToolCapabilityPackRegistry : IToolCapabilityPackRegistry
{
    private readonly IReadOnlyDictionary<string, IToolCapabilityPack> _packsById;

    public ToolCapabilityPackRegistry(IEnumerable<IToolCapabilityPack> packs)
    {
        _packsById = packs.ToDictionary(p => p.PackId, StringComparer.OrdinalIgnoreCase);
        All = _packsById.Values.ToList();
    }

    public IReadOnlyList<IToolCapabilityPack> All { get; }

    public IToolCapabilityPack? Get(string packId) =>
        _packsById.TryGetValue(packId, out var pack) ? pack : null;

    public string? ResolveTemplate(IReadOnlyList<string> enabledPackIds, string templateId)
    {
        foreach (var packId in enabledPackIds)
        {
            if (!_packsById.TryGetValue(packId, out var pack))
                continue;

            if (pack.DefaultTemplates.TryGetValue(templateId, out var template)
                && !string.IsNullOrWhiteSpace(template))
            {
                return template.Trim();
            }
        }

        return null;
    }
}
