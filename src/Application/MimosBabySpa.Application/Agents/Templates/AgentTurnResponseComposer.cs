using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Agents.Templates;

public sealed class AgentTurnResponseComposer : IAgentTurnResponseComposer
{
    private readonly IAgentTemplateResolver _templateResolver;
    private readonly ITemplateRenderer _renderer;
    private readonly ILogger<AgentTurnResponseComposer> _logger;

    public AgentTurnResponseComposer(
        IAgentTemplateResolver templateResolver,
        ITemplateRenderer renderer,
        ILogger<AgentTurnResponseComposer> logger)
    {
        _templateResolver = templateResolver;
        _renderer = renderer;
        _logger = logger;
    }

    public string Compose(
        AgentConfig config,
        IReadOnlyList<Tools.IAgentTool> enabledTools,
        string llmResponse,
        IEnumerable<TurnFragmentEntry> fragments)
    {
        var fragmentList = fragments.ToList();
        if (fragmentList.Count == 0)
            return (llmResponse ?? string.Empty).Trim();

        var inlineResult = llmResponse ?? string.Empty;
        var exclusiveParts = new List<string>();

        foreach (var entry in fragmentList)
        {
            var template = _templateResolver.Resolve(config, entry.Fragment.TemplateId, enabledTools);

            if (template is null)
            {
                _logger.LogWarning(
                    "Template '{TemplateId}' not found in agent SettingsJson.templates",
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
                    "Required token {Token} not found in LLM response — using rendered template only",
                    entry.Token);
                inlineResult = rendered;
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
