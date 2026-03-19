using System.Text;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.GenericFlow.Services;

/// <summary>
/// Injects KnowledgeSource content into the LLM prompt.
///
/// The engine does NOT interpret or reformat the content — it renders it as-is.
/// Content formatting, structure, and language are entirely the responsibility
/// of whoever configures the KnowledgeSource in the database.
///
/// The KnowledgeSourceType is metadata for the admin UI only; the engine ignores it.
/// </summary>
public class KnowledgeSourceRenderer
{
    public string Render(KnowledgeSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Content)) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"# {source.Name}");
        sb.AppendLine(source.Content.Trim());
        sb.AppendLine();
        return sb.ToString();
    }

    public string RenderMany(IEnumerable<KnowledgeSource> sources)
    {
        var parts = sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Content))
            .Select(Render);
        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>
    /// Compact rendering for extraction prompts: returns only section headings (## / ###)
    /// as a comma-separated list of service names. This avoids injecting full descriptive
    /// content (benefits, includes, prices) that can trigger content filters while still
    /// giving the LLM the exact names it needs to resolve aliases.
    /// </summary>
    public string RenderNamesOnly(KnowledgeSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Content)) return string.Empty;

        var names = source.Content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("###") || line.StartsWith("##"))
            .Select(line => line.TrimStart('#').Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(", ", names);
    }
}
