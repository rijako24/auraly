using MimosBabySpa.Application.Common.Interfaces;

namespace Auraly.Api;

internal sealed class AuralyExecutionContextAccessorAdapter(ITenantContext context)
    : IAuralyExecutionContextAccessor
{
    public void SetTenant(Guid tenantId) => context.SetTenant(tenantId);

    public void SetBusiness(Guid businessId) => context.SetBusiness(businessId);

    public void SetUser(Guid userId) => context.SetUser(userId);
}
