using MimosBabySpa.Application.Agents.Facts;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Extensiones de AgentToolContext para acceso a facts por rol semántico.
/// Punto de migración desde ConversationFactKeys hardcoded hacia roles dinámicos.
/// </summary>
public static class AgentToolContextExtensions
{
    /// <summary>
    /// Obtiene el valor de un fact por su rol semántico (ej. "customer.name").
    /// Usa el FactRoleIndex construido desde el factSchema del tenant.
    /// Devuelve null si el rol no existe o el fact está vacío.
    /// </summary>
    public static string? GetFactByRole(this AgentToolContext ctx, string role)
    {
        if (ctx.Config?.FactSchema is null)
            return null;

        var index = new FactRoleIndex(ctx.Config.FactSchema);
        return index.GetByRole(ctx.Facts, role);
    }

    /// <summary>
    /// Resuelve el key canónico de un rol semántico según el schema del tenant.
    /// Útil para tools que necesitan el key canónico (ej. para set_fact).
    /// </summary>
    public static string? GetKeyByRole(this AgentToolContext ctx, string role)
    {
        if (ctx.Config?.FactSchema is null)
            return null;

        var index = new FactRoleIndex(ctx.Config.FactSchema);
        return index.KeyByRole(role);
    }
}
