using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Domain.Authorization;

namespace Auraly.Application.Authorization;

public interface IUserPermissionSetProvider
{
    UserPermissionSet Get(TenantId tenantId, UserId userId);
}

public sealed class PermissionAuthorizer(IUserPermissionSetProvider provider) : IPermissionAuthorizer
{
    public void Demand(TenantId tenantId, UserId userId, string permission)
    {
        var permissionSet = provider.Get(tenantId, userId);
        if (permissionSet.TenantId != tenantId || permissionSet.UserId != userId)
        {
            throw new InvalidOperationException("The permission provider returned a set for another user.");
        }

        permissionSet.Demand(permission);
    }
}
