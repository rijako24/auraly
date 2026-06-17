namespace MimosBabySpa.Application.Agents.Configuration;

public static class FactScopes
{
    public const string Customer = "customer";
    public const string Request = "request";
    public const string Ephemeral = "ephemeral";
}

public static class FactScopeRules
{
    public static string EffectiveScope(this FactSchemaEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Scope))
            return entry.Scope.Trim().ToLowerInvariant();

        return entry.Source.Equals("system", StringComparison.OrdinalIgnoreCase)
            || entry.Source.Equals("session", StringComparison.OrdinalIgnoreCase)
            ? FactScopes.Ephemeral
            : FactScopes.Request;
    }

    public static bool IsCustomerScoped(this FactSchemaEntry entry) =>
        entry.EffectiveScope().Equals(FactScopes.Customer, StringComparison.OrdinalIgnoreCase);

    public static bool ShouldRememberAcrossRequests(this FactSchemaEntry entry) =>
        entry.IsCustomerScoped();

    public static TimeSpan? Retention(this FactSchemaEntry entry) =>
        entry.RetentionDays is > 0
            ? TimeSpan.FromDays(entry.RetentionDays.Value)
            : null;
}
