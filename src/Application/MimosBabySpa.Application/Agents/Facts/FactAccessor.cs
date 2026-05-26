namespace MimosBabySpa.Application.Agents.Facts;

public sealed class FactAccessor : IFactAccessor
{
    public string? GetByRole(AgentToolContext ctx, string role)
    {
        var index = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        return index.GetByRole(ctx.Facts, role);
    }

    public string? GetRoleForKey(AgentToolContext ctx, string canonicalKey)
    {
        var index = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        return index.RoleForKey(canonicalKey);
    }

    public string? GetKeyByRole(AgentToolContext ctx, string role)
    {
        var index = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        return index.KeyByRole(role);
    }
}
