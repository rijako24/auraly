namespace Auraly.Platform.Application.Agents.Templates;

public interface IPromptTemplateExtractor
{
    /// <summary>
    /// Extrae la plantilla declarada como [template: id] seguida de un bloque ``` ... ```.
    /// </summary>
    string? Extract(string systemPromptMarkdown, string templateId);
}
