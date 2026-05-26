namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// Acceso a facts por rol semántico según el factSchema del tenant.
/// </summary>
public interface IFactAccessor
{
    string? GetByRole(AgentToolContext ctx, string role);
    string? GetRoleForKey(AgentToolContext ctx, string canonicalKey);
    string? GetKeyByRole(AgentToolContext ctx, string role);
}
