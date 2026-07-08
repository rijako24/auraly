namespace MimosBabySpa.Application.Agents.Configuration;

public sealed class ConversationalFlowLanguage
{
    public bool Enabled { get; init; }

    public IReadOnlyDictionary<string, SemanticFlowAction> Actions { get; init; }
        = new Dictionary<string, SemanticFlowAction>(StringComparer.OrdinalIgnoreCase);
}

public sealed class SemanticFlowAction
{
    public string Name { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public string Tool { get; init; } = string.Empty;

    public IReadOnlyList<string> Requires { get; init; } = [];

    public IReadOnlyList<string> Produces { get; init; } = [];

    public string? WhenMissingData { get; init; }

    public string? OnSuccess { get; init; }

    public string? OnProblem { get; init; }
}
