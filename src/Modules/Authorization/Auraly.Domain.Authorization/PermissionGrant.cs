using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Domain.Authorization;

public sealed class UserPermissionSet
{
    private readonly HashSet<string> _permissions;

    public UserPermissionSet(TenantId tenantId, UserId userId, IEnumerable<string> permissions)
    {
        if (tenantId.Value == Guid.Empty) throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        if (userId.Value == Guid.Empty) throw new ArgumentException("A user ID is required.", nameof(userId));

        TenantId = tenantId;
        UserId = userId;
        _permissions = new HashSet<string>(
            permissions.Where(permission => !string.IsNullOrWhiteSpace(permission)),
            StringComparer.Ordinal);
    }

    public TenantId TenantId { get; }
    public UserId UserId { get; }

    public bool Allows(string permission) => _permissions.Contains(permission);

    public void Demand(string permission)
    {
        if (!Allows(permission))
        {
            throw new UnauthorizedAccessException(
                $"User '{UserId}' does not have permission '{permission}'.");
        }
    }
}
