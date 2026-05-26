namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class PromptSection
{
    public string Id { get; init; } = string.Empty;
    public int Order { get; init; }
    public string Content { get; init; } = string.Empty;
}
