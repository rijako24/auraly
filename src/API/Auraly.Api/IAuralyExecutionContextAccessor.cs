namespace Auraly.Api;

internal interface IAuralyExecutionContextAccessor
{
    void SetTenant(Guid tenantId);
    void SetBusiness(Guid businessId);
    void SetUser(Guid userId);
}
