namespace Auraly.Contracts.Authorization;

public static class PosDelegatedPermissionPolicy
{
    public const int MaximumResourceLength = 100;

    public static bool IsValid(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource) ||
            resource.Length > MaximumResourceLength ||
            !string.Equals(resource, resource.Trim(), StringComparison.Ordinal))
            return false;

        return resource.All(character =>
            character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '.' or '-' or '_');
    }
}
