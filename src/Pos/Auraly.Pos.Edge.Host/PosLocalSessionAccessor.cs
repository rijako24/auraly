using Auraly.Application.Authorization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Authorization;

namespace Auraly.Pos.Edge.Host;

public sealed class PosLocalSessionAccessor
{
    private readonly AsyncLocal<PosLocalUserSession?> _current = new();

    public PosLocalUserSession? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    public PosLocalUserSession Required() =>
        Current ?? throw new UnauthorizedAccessException(
            "A valid local cashier session is required.");
}

internal sealed class PosLocalPermissionProvider(
    PosLocalSessionAccessor sessions) : IUserPermissionSetProvider
{
    public UserPermissionSet Get(TenantId tenantId, UserId userId)
    {
        var session = sessions.Required();
        if (session.UserId != userId.Value)
            throw new UnauthorizedAccessException(
                "The local cashier session does not match the sale.");
        return new UserPermissionSet(tenantId, userId, session.Permissions);
    }
}
