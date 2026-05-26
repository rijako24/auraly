namespace MimosBabySpa.Application.Agents.Packs;

public interface IToolCapabilityPackRegistry
{
    IReadOnlyList<IToolCapabilityPack> All { get; }

    IToolCapabilityPack? Get(string packId);

    string? ResolveTemplate(
        IReadOnlyList<string> enabledPackIds,
        string templateId);
}
