using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Agents.Templates;

public sealed class AgentTurnResponseComposer : IAgentTurnResponseComposer
{
    private readonly IPromptTemplateExtractor _extractor;
    private readonly ITemplateRenderer _renderer;
    private readonly ILogger<AgentTurnResponseComposer> _logger;

    public AgentTurnResponseComposer(
        IPromptTemplateExtractor extractor,
        ITemplateRenderer renderer,
        ILogger<AgentTurnResponseComposer> logger)
    {
        _extractor = extractor;
        _renderer = renderer;
        _logger = logger;
    }

    public string Compose(string agentSystemPrompt, string llmResponse, IEnumerable<TurnFragmentEntry> fragments)
    {
        var fragmentList = fragments.ToList();
        if (fragmentList.Count == 0)
            return (llmResponse ?? string.Empty).Trim();

        var inlineResult = llmResponse ?? string.Empty;
        var exclusiveParts = new List<string>();

        foreach (var entry in fragmentList)
        {
            var template = _extractor.Extract(agentSystemPrompt, entry.Fragment.TemplateId)
                ?? TemplateFallbackCatalog.Get(entry.Fragment.TemplateId);

            if (template is null)
            {
                _logger.LogWarning(
                    "Template '{TemplateId}' not found in agent prompt and no fallback available",
                    entry.Fragment.TemplateId);
                continue;
            }

            var rendered = _renderer.Render(template, entry.Fragment.Data);

            if (entry.Fragment.Mode == FragmentRenderMode.Exclusive)
            {
                exclusiveParts.Add(rendered);
                continue;
            }

            if (inlineResult.Contains(entry.Token, StringComparison.Ordinal))
                inlineResult = inlineResult.Replace(entry.Token, rendered, StringComparison.Ordinal);
            else if (entry.Fragment.Priority == FragmentPriority.Required)
            {
                _logger.LogDebug(
                    "Required token {Token} not found in LLM response — prepending rendered template",
                    entry.Token);
                inlineResult = string.IsNullOrWhiteSpace(inlineResult)
                    ? rendered
                    : $"{rendered}{Environment.NewLine}{Environment.NewLine}{inlineResult}";
            }
            else
            {
                _logger.LogDebug(
                    "Optional token {Token} not referenced — skipping fragment {TemplateId}",
                    entry.Token,
                    entry.Fragment.TemplateId);
            }
        }

        if (exclusiveParts.Count > 0)
            return string.Join($"{Environment.NewLine}{Environment.NewLine}", exclusiveParts).Trim();

        return inlineResult.Trim();
    }
}
