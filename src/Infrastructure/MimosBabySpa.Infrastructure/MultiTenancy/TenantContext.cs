using MimosBabySpa.Application.Common.Interfaces;

namespace MimosBabySpa.Infrastructure.MultiTenancy;

public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? BusinessId { get; private set; }
    public Guid? UserId { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
    public void SetBusiness(Guid businessId) => BusinessId = businessId;
    public void SetUser(Guid userId) => UserId = userId;
}
