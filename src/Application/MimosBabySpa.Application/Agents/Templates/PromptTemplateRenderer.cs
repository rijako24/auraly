using System.Text.RegularExpressions;

namespace MimosBabySpa.Application.Agents.Templates;

/// <summary>
/// Renderizador minimalista para plantillas del prompt: {{var}}, {{#if var}}...{{else}}, {{#each list}}.
/// </summary>
public sealed partial class PromptTemplateRenderer : ITemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var result = template;
        result = RenderEachBlocks(result, data);
        result = RenderIfBlocks(result, data);
        result = ReplaceVariables(result, data);
        return NormalizeBlankLines(result.TrimEnd());
    }

    private static string RenderEachBlocks(string input, IReadOnlyDictionary<string, object?> data)
    {
        return EachBlockRegex().Replace(input, match =>
        {
            var listName = match.Groups["name"].Value;
            var inner = match.Groups["body"].Value.Trim();

            if (!data.TryGetValue(listName, out var raw) || raw is null)
                return string.Empty;

            if (raw is not System.Collections.IEnumerable items || raw is string)
                return string.Empty;

            var renderedItems = new List<string>();
            foreach (var item in items)
            {
                if (item is null)
                    continue;
                var scope = MergeScope(data, item);
                var block = inner;
                block = RenderIfBlocks(block, scope);
                block = ReplaceVariables(block, scope);
                renderedItems.Add(block);
            }

            return renderedItems.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, renderedItems);
        });
    }

    private static string RenderIfBlocks(string input, IReadOnlyDictionary<string, object?> data)
    {
        if (!IfBlockRegex().IsMatch(input))
            return input;

        return IfBlockRegex().Replace(input, match =>
        {
            var name = match.Groups["name"].Value;
            var inner = match.Groups["body"].Value.Trim();

            var branches = ElseRegex().Split(inner, 2);
            inner = IsTruthy(data, name)
                ? branches[0].Trim()
                : branches.Length > 1 ? branches[1].Trim() : string.Empty;
            if (inner.Length == 0)
                return string.Empty;

            inner = RenderEachBlocks(inner, data);
            inner = RenderIfBlocks(inner, data);
            inner = ReplaceVariables(inner, data);
            return inner;
        });
    }

    private static string ReplaceVariables(string input, IReadOnlyDictionary<string, object?> data)
    {
        return VariableRegex().Replace(input, match =>
        {
            var name = match.Groups["name"].Value;
            return data.TryGetValue(name, out var value) ? TemplateValueFormatter.Format(name, value) : string.Empty;
        });
    }

    private static string NormalizeBlankLines(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd(' ', '\t'))
            .ToList();

        var normalized = new List<string>(lines.Count);
        var previousBlank = false;

        foreach (var line in lines)
        {
            var isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank && previousBlank)
                continue;

            normalized.Add(line);
            previousBlank = isBlank;
        }

        while (normalized.Count > 0 && string.IsNullOrWhiteSpace(normalized[^1]))
            normalized.RemoveAt(normalized.Count - 1);

        return string.Join(Environment.NewLine, normalized);
    }

    private static IReadOnlyDictionary<string, object?> MergeScope(
        IReadOnlyDictionary<string, object?> parent,
        object item)
    {
        var merged = new Dictionary<string, object?>(parent, StringComparer.OrdinalIgnoreCase);

        if (item is IReadOnlyDictionary<string, object?> dict)
        {
            foreach (var (k, v) in dict)
                merged[k] = v;
            return merged;
        }

        if (item is string or ValueType)
        {
            merged["this"] = item;
            return merged;
        }

        foreach (var prop in item.GetType().GetProperties())
            merged[prop.Name] = prop.GetValue(item);

        return merged;
    }

    private static bool IsTruthy(IReadOnlyDictionary<string, object?> data, string name)
    {
        if (!data.TryGetValue(name, out var value) || value is null)
            return false;

        return value switch
        {
            string s => !string.IsNullOrWhiteSpace(s),
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            decimal d => d != 0,
            double d => Math.Abs(d) > double.Epsilon,
            IEnumerable<object> e => e.Any(),
            _ => true
        };
    }

    [GeneratedRegex(@"\{\{#each\s+(?<name>[\w_]+)\}\}(?<body>[\s\S]*?)\{\{/each\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex EachBlockRegex();

    [GeneratedRegex(@"\{\{#if\s+(?<name>[\w_]+)\}\}(?<body>[\s\S]*?)\{\{/if\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex IfBlockRegex();
    [GeneratedRegex(@"\{\{else\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex ElseRegex();


    [GeneratedRegex(@"\{\{(?<name>[\w_]+)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex VariableRegex();
}
