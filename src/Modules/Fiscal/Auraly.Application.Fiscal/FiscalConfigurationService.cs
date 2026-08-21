using Auraly.Contracts.Fiscal;

namespace Auraly.Application.Fiscal;

public sealed record FiscalConfigurationUser(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public interface IFiscalConfigurationStore
{
    Task<FiscalResolutionConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken);
}

public interface IFiscalDeviceSeriesStore
{
    Task<FiscalDeviceSeriesWorkspace> ListAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken);
    Task<FiscalDeviceSeriesWorkspace> AssignAsync(
        Guid tenantId, Guid businessId, AssignFiscalDeviceSeriesRequest request,
        CancellationToken cancellationToken);
    Task<PosFiscalSeriesProvisioning?> GetProvisioningAsync(
        Guid tenantId, Guid businessId, Guid deviceId,
        CancellationToken cancellationToken);
}

public sealed class FiscalDeviceSeriesService(IFiscalDeviceSeriesStore store)
{
    public Task<FiscalDeviceSeriesWorkspace> ListAsync(
        FiscalConfigurationUser user, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationRead);
        ValidateBusiness(businessId);
        return store.ListAsync(user.TenantId, businessId, cancellationToken);
    }

    public Task<FiscalDeviceSeriesWorkspace> AssignAsync(
        FiscalConfigurationUser user, Guid businessId,
        AssignFiscalDeviceSeriesRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        if (request.DeviceId == Guid.Empty)
            throw new FiscalConfigurationValidationException("El equipo enrolado es obligatorio.");
        if (request.ConsecutiveCount < 1)
            throw new FiscalConfigurationValidationException(
                "La cantidad de consecutivos debe ser mayor o igual a uno.");
        return store.AssignAsync(user.TenantId, businessId, request, cancellationToken);
    }

    public Task<PosFiscalSeriesProvisioning?> GetProvisioningAsync(
        Guid tenantId, Guid businessId, Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        ValidateBusiness(businessId);
        if (tenantId == Guid.Empty || deviceId == Guid.Empty)
            throw new FiscalConfigurationForbiddenException(
                "La identidad del equipo no es válida.");
        return store.GetProvisioningAsync(
            tenantId, businessId, deviceId, cancellationToken);
    }

    private static void ValidateBusiness(Guid businessId)
    {
        if (businessId == Guid.Empty)
            throw new FiscalConfigurationValidationException("La sede es obligatoria.");
    }

    private static void Demand(FiscalConfigurationUser user, string permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(permission))
            throw new FiscalConfigurationForbiddenException(
                $"Permission '{permission}' is required.");
    }
}

public sealed class FiscalConfigurationValidationException(string message) : Exception(message);
public sealed class FiscalConfigurationForbiddenException(string message) : Exception(message);

public sealed class FiscalConfigurationService(IFiscalConfigurationStore store)
{
    public Task<FiscalResolutionConfiguration> GetAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationRead);
        if (businessId == Guid.Empty)
            throw new FiscalConfigurationValidationException("La sede es obligatoria.");
        return store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    private static void Demand(FiscalConfigurationUser user, string permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(permission))
            throw new FiscalConfigurationForbiddenException($"Permission '{permission}' is required.");
    }
}
