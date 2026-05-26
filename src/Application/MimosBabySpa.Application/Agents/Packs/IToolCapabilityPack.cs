using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Packs;

/// <summary>
/// Capability pack: agrupa tools, plantillas default y contexto de dominio.
/// </summary>
public interface IToolCapabilityPack
{
    string PackId { get; }

    IReadOnlyList<string> ToolNames { get; }

    IReadOnlyDictionary<string, string> DefaultTemplates { get; }
}
