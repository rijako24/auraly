namespace MimosBabySpa.Application.Agents.Templates;

public interface ITemplateRenderer
{
    string Render(string template, IReadOnlyDictionary<string, object?> data);
}
