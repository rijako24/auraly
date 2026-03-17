namespace MimosBabySpa.Application.Common.Interfaces;

public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? BusinessId { get; }
    Guid? UserId { get; }
    void SetTenant(Guid tenantId);
    void SetBusiness(Guid businessId);
    void SetUser(Guid userId);
}
