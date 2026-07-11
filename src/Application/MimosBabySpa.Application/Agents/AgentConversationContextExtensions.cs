using MimosBabySpa.Application.Agents.Facts;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Extensiones de AgentConversationContext para acceso a facts por rol semántico.
/// Punto de migración desde ConversationFactKeys hardcoded hacia roles dinámicos.
/// </summary>
public static class AgentConversationContextExtensions
{
    /// <summary>
    /// Obtiene el valor de un fact por su rol semántico (ej. "customer.name").
    /// Usa el FactRoleIndex construido desde el factSchema del tenant.
    /// Devuelve null si el rol no existe o el fact está vacío.
    /// </summary>
    public static string? GetFactByRole(this AgentConversationContext ctx, string role)
    {
        if (ctx.Config?.FactSchema is null)
            return null;

        var index = new FactRoleIndex(ctx.Config.FactSchema);
        return index.GetByRole(ctx.Facts, role);
    }

    /// <summary>
    /// Resuelve el key canónico de un rol semántico según el schema del tenant.
    /// Útil para operations que necesitan el key canónico (por ejemplo, al persistir un fact).
    /// </summary>
    public static string? GetKeyByRole(this AgentConversationContext ctx, string role)
    {
        if (ctx.Config?.FactSchema is null)
            return null;

        var index = new FactRoleIndex(ctx.Config.FactSchema);
        return index.KeyByRole(role);
    }
}
