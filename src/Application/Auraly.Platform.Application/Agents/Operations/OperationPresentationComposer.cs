using Auraly.Platform.Application.Agents.Templates;

namespace Auraly.Platform.Application.Agents.Operations;

public interface IOperationPresentationComposer
{
    string Compose(
        AgentConfig config,
        string? llmResponse,
        IReadOnlyList<OperationPresentation> presentations);
}

public sealed class OperationPresentationComposer : IOperationPresentationComposer
{
    private readonly IAgentTemplateResolver _templates;
    private readonly ITemplateRenderer _renderer;

    public OperationPresentationComposer(
        IAgentTemplateResolver templates,
        ITemplateRenderer renderer)
    {
        _templates = templates;
        _renderer = renderer;
    }

    public string Compose(
        AgentConfig config,
        string? llmResponse,
        IReadOnlyList<OperationPresentation> presentations)
    {
        if (presentations.Count == 0)
            return (llmResponse ?? string.Empty).Trim();

        var exclusive = presentations
            .Where(value => value.Mode == FragmentRenderMode.Exclusive)
            .Select(value => Render(config, value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (exclusive.Count > 0)
            return string.Join($"{Environment.NewLine}{Environment.NewLine}", exclusive).Trim();

        var required = presentations
            .Where(value => value.Priority == FragmentPriority.Required)
            .Select(value => Render(config, value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (required.Count == 0)
            return (llmResponse ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(llmResponse))
            required.Add(llmResponse.Trim());
        return string.Join($"{Environment.NewLine}{Environment.NewLine}", required).Trim();
    }

    private string Render(AgentConfig config, OperationPresentation presentation)
    {
        var template = _templates.Resolve(config, presentation.TemplateId);
        return string.IsNullOrWhiteSpace(template)
            ? string.Empty
            : _renderer.Render(template, presentation.Data);
    }
}
