using Auraly.Contracts.TenantBilling;
using Auraly.Contracts.Tenants;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class TenantProvisioner(
    IUnitOfWork unitOfWork,
    ITenantProvisioningStore provisioning,
    ILogger<TenantProvisioner> logger) : ITenantProvisioner
{
    public async Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        Guid? actorUserId,
        TenantQuoteDto commercialQuote,
        CancellationToken cancellationToken = default)
    {
        TenantProvisioningRequestValidator.Validate(request);
        if (!await unitOfWork.Tenants.IsReferenceOptionActiveAsync(
                "tenant-entity-type", request.EntityType, cancellationToken)
            || !await unitOfWork.Tenants.IsReferenceOptionActiveAsync(
                "tenant-identification-type", request.IdentificationTypeCode,
                cancellationToken))
        {
            throw new ArgumentException(
                "Selecciona un tipo de persona y de identificación vigentes.");
        }

        ArgumentNullException.ThrowIfNull(commercialQuote);
        if (request.MaximumUsers != checked(
                commercialQuote.FullUserLimit + commercialQuote.SellerUserLimit)
            || request.MaximumEnrolledDevices != commercialQuote.PosDeviceLimit)
        {
            throw new ArgumentException(
                "Los cupos del tenant no coinciden con la cotización aprobada.");
        }

        var result = await provisioning.ProvisionAsync(
            request, actorUserId, commercialQuote, cancellationToken);
        logger.LogInformation(
            "Tenant {TenantId} provisioned with business {BusinessId}",
            result.TenantId,
            result.BusinessId);
        return result;
    }
}
