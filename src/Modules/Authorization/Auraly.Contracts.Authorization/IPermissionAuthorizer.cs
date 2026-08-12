using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Contracts.Authorization;

public interface IPermissionAuthorizer
{
    void Demand(TenantId tenantId, UserId userId, string permission);
}
