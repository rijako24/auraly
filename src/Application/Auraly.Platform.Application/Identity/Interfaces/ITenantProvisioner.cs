using Auraly.Contracts.TenantBilling;
using Auraly.Contracts.Tenants;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface ITenantProvisioner
{
    Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        Guid? actorUserId,
        TenantQuoteDto commercialQuote,
        CancellationToken cancellationToken = default);
}
