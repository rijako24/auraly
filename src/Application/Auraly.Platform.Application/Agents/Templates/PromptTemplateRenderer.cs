using System.Collections;
using System.Text;

namespace Auraly.Platform.Application.Agents.Templates;

/// <summary>
/// Renderizador minimalista para plantillas del prompt: {{var}}, {{#if var}}...{{else}}, {{#each list}}.
/// Los bloques se analizan de forma recursiva para admitir anidamiento sin exponer tags al cliente.
/// </summary>
public sealed class PromptTemplateRenderer : ITemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var result = RenderSection(NormalizeEscapedNewlines(template), data);
        return NormalizeBlankLines(result.TrimEnd());
    }

    private static string RenderSection(string input, IReadOnlyDictionary<string, object?> data)
    {
        var result = new StringBuilder(input.Length);
        var position = 0;
        while (position < input.Length)
        {
            var tagStart = input.IndexOf("{{", position, StringComparison.Ordinal);
            if (tagStart < 0)
            {
                result.Append(input, position, input.Length - position);
                break;
            }

            result.Append(input, position, tagStart - position);
            if (!TryReadTag(input, tagStart, out var tag))
            {
                result.Append(input, tagStart, input.Length - tagStart);
                break;
            }

            if (tag.Token.StartsWith("#if ", StringComparison.OrdinalIgnoreCase))
            {
                var name = tag.Token[4..].Trim();
                var block = FindBlock(input, tag.End, "if");
                var branchEnd = block.ElseStart ?? block.CloseStart;
                var selected = IsTruthy(data, name)
                    ? input[tag.End..branchEnd]
                    : block.ElseEnd is int elseEnd
                        ? input[elseEnd..block.CloseStart]
                        : string.Empty;
                result.Append(RenderSection(selected.Trim(), data));
                position = block.CloseEnd;
                continue;
            }

            if (tag.Token.StartsWith("#each ", StringComparison.OrdinalIgnoreCase))
            {
                var name = tag.Token[6..].Trim();
                var block = FindBlock(input, tag.End, "each");
                var body = input[tag.End..block.CloseStart].Trim();
                if (data.TryGetValue(name, out var raw)
                    && raw is IEnumerable items
                    && raw is not string)
                {
                    var renderedItems = new List<string>();
                    foreach (var item in items)
                    {
                        if (item is not null)
                            renderedItems.Add(RenderSection(body, MergeScope(data, item)));
                    }

                    result.AppendJoin(Environment.NewLine, renderedItems.Where(value => value.Length > 0));
                }

                position = block.CloseEnd;
                continue;
            }

            if (tag.Token is "else" or "/if" or "/each")
                throw new FormatException($"Unexpected template tag '{{{{{tag.Token}}}}}'.");

            result.Append(data.TryGetValue(tag.Token, out var value)
                ? TemplateValueFormatter.Format(tag.Token, value)
                : string.Empty);
            position = tag.End;
        }

        return result.ToString();
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

    private static string NormalizeEscapedNewlines(string template) =>
        template
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);

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
            IEnumerable enumerable => enumerable.Cast<object?>().Any(),
            _ => true
        };
    }

    private static TemplateBlock FindBlock(string input, int contentStart, string blockName)
    {
        var depth = 1;
        int? elseStart = null;
        int? elseEnd = null;
        var position = contentStart;
        while (position < input.Length)
        {
            var tagStart = input.IndexOf("{{", position, StringComparison.Ordinal);
            if (tagStart < 0 || !TryReadTag(input, tagStart, out var tag))
                break;

            if (tag.Token.Equals($"#{blockName}", StringComparison.OrdinalIgnoreCase)
                || tag.Token.StartsWith($"#{blockName} ", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (tag.Token.Equals($"/{blockName}", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth == 0)
                    return new TemplateBlock(tagStart, tag.End, elseStart, elseEnd);
            }
            else if (blockName.Equals("if", StringComparison.OrdinalIgnoreCase)
                && depth == 1
                && tag.Token.Equals("else", StringComparison.OrdinalIgnoreCase))
            {
                elseStart = tagStart;
                elseEnd = tag.End;
            }

            position = tag.End;
        }

        throw new FormatException($"Template block '{{{{#{blockName}}}}}' is not closed.");
    }

    private static bool TryReadTag(string input, int start, out TemplateTag tag)
    {
        var end = input.IndexOf("}}", start + 2, StringComparison.Ordinal);
        if (end < 0)
        {
            tag = default;
            return false;
        }

        tag = new TemplateTag(input[(start + 2)..end].Trim(), end + 2);
        return true;
    }

    private readonly record struct TemplateTag(string Token, int End);
    private readonly record struct TemplateBlock(
        int CloseStart,
        int CloseEnd,
        int? ElseStart,
        int? ElseEnd);
}
