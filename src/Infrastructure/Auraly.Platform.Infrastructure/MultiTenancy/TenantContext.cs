using Auraly.Platform.Application.Common.Interfaces;

namespace Auraly.Platform.Infrastructure.MultiTenancy;

public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? BusinessId { get; private set; }
    public Guid? UserId { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
    public void SetBusiness(Guid businessId) => BusinessId = businessId;
    public void SetUser(Guid userId) => UserId = userId;
}
