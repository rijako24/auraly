using System.Text.RegularExpressions;

namespace MimosBabySpa.Application.Agents.Templates;

public sealed partial class PromptTemplateExtractor : IPromptTemplateExtractor
{
    public string? Extract(string systemPromptMarkdown, string templateId)
    {
        if (string.IsNullOrWhiteSpace(systemPromptMarkdown) || string.IsNullOrWhiteSpace(templateId))
            return null;

        var match = TemplateBlockRegex().Match(systemPromptMarkdown);
        while (match.Success)
        {
            if (match.Groups["id"].Value.Equals(templateId, StringComparison.OrdinalIgnoreCase))
                return match.Groups["body"].Value.TrimEnd();

            match = match.NextMatch();
        }

        return null;
    }

    [GeneratedRegex(
        @"\[template:\s*(?<id>[\w_]+)\]\s*```\s*\r?\n(?<body>[\s\S]*?)\r?\n```",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemplateBlockRegex();
}
