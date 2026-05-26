using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Facts;

public sealed class RoleFactResolver : IRoleFactResolver
{
    public IReadOnlyDictionary<string, string> Resolve(IAgentTool tool, AgentToolContext ctx)
    {
        if (tool.RoleRequirements.Count == 0)
            return EmptyFacts.Instance;

        var index = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in tool.RoleRequirements)
        {
            var value = index.GetByRole(ctx.Facts, requirement.Role);
            if (!string.IsNullOrWhiteSpace(value))
                resolved[requirement.Role] = value.Trim();
        }

        return resolved;
    }

    private sealed class EmptyFacts : IReadOnlyDictionary<string, string>
    {
        public static readonly EmptyFacts Instance = new();
        public string this[string key] => throw new KeyNotFoundException(key);
        public IEnumerable<string> Keys => [];
        public IEnumerable<string> Values => [];
        public int Count => 0;
        public bool ContainsKey(string key) => false;
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool TryGetValue(string key, out string value) { value = null!; return false; }
    }
}
