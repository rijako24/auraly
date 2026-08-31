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
        Guid tenantId, Guid businessId, Guid userId,
        AssignFiscalDeviceSeriesRequest request,
        CancellationToken cancellationToken);
    Task<FiscalDeviceSeriesWorkspace> UnassignAsync(
        Guid tenantId, Guid businessId,
        UnassignFiscalDeviceSeriesRequest request,
        CancellationToken cancellationToken);
    Task<FiscalDeviceSeriesWorkspace> SaveAlertSettingsAsync(
        Guid tenantId, Guid businessId, Guid userId,
        SaveFiscalResolutionAlertSettingsRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PosFiscalSeriesProvisioning>> GetProvisioningAsync(
        Guid tenantId, Guid businessId, Guid deviceId, Guid? currentSeriesId,
        long? nextConsecutive,
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
        if (request.DianNumberingRangeId == Guid.Empty)
            throw new FiscalConfigurationValidationException("La resolución DIAN es obligatoria.");
        return store.AssignAsync(
            user.TenantId, businessId, user.UserId, request, cancellationToken);
    }

    public Task<IReadOnlyList<PosFiscalSeriesProvisioning>> GetProvisioningAsync(
        Guid tenantId, Guid businessId, Guid deviceId, Guid? currentSeriesId,
        long? nextConsecutive,
        CancellationToken cancellationToken = default)
    {
        ValidateBusiness(businessId);
        if (tenantId == Guid.Empty || deviceId == Guid.Empty)
            throw new FiscalConfigurationForbiddenException(
                "La identidad del equipo no es válida.");
        if (currentSeriesId.HasValue != nextConsecutive.HasValue || nextConsecutive < 1)
            throw new FiscalConfigurationValidationException(
                "El cursor fiscal local debe informarse completo y ser positivo.");
        return store.GetProvisioningAsync(
            tenantId, businessId, deviceId, currentSeriesId, nextConsecutive,
            cancellationToken);
    }

    public Task<FiscalDeviceSeriesWorkspace> UnassignAsync(
        FiscalConfigurationUser user, Guid businessId,
        UnassignFiscalDeviceSeriesRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeviceId == Guid.Empty)
            throw new FiscalConfigurationValidationException("El equipo enrolado es obligatorio.");
        return store.UnassignAsync(
            user.TenantId, businessId, request, cancellationToken);
    }

    public Task<FiscalDeviceSeriesWorkspace> SaveAlertSettingsAsync(
        FiscalConfigurationUser user, Guid businessId,
        SaveFiscalResolutionAlertSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpirationWarningDays is < 0 or > 365)
            throw new FiscalConfigurationValidationException(
                "Los días de alerta deben estar entre 0 y 365.");
        if (request.RemainingNumberWarningThreshold is < 0 or > 1_000_000_000)
            throw new FiscalConfigurationValidationException(
                "El umbral de numeración debe estar entre 0 y 1.000.000.000.");
        return store.SaveAlertSettingsAsync(
            user.TenantId, businessId, user.UserId, request, cancellationToken);
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
