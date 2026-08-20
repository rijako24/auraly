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
    Task<FiscalResolutionConfiguration> SaveAsync(
        Guid tenantId,
        Guid businessId,
        SaveFiscalResolutionConfiguration request,
        CancellationToken cancellationToken);
}

public interface ISalesInvoiceNumberingConfigurationStore
{
    Task<SalesInvoiceNumberingConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken);
    Task<SalesInvoiceNumberingConfiguration> SaveAsync(
        Guid tenantId, Guid businessId, long initialConsecutive,
        CancellationToken cancellationToken);
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

public sealed class SalesInvoiceNumberingConfigurationService(
    ISalesInvoiceNumberingConfigurationStore store)
{
    public Task<SalesInvoiceNumberingConfiguration> GetAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationRead);
        if (businessId == Guid.Empty)
            throw new FiscalConfigurationValidationException("La sede es obligatoria.");
        return store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    public Task<SalesInvoiceNumberingConfiguration> SaveAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        SaveSalesInvoiceNumberingConfiguration request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        if (businessId == Guid.Empty)
            throw new FiscalConfigurationValidationException("La sede es obligatoria.");
        if (request.InitialConsecutive < 1)
            throw new FiscalConfigurationValidationException(
                "El primer consecutivo debe ser mayor o igual a uno.");
        return store.SaveAsync(
            user.TenantId, businessId, request.InitialConsecutive, cancellationToken);
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

public sealed class FiscalConfigurationService(
    IFiscalConfigurationStore store,
    ISalesInvoiceNumberingConfigurationStore numbering)
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

    public async Task<FiscalResolutionConfiguration> SaveAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        SaveFiscalResolutionConfiguration request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        if (businessId == Guid.Empty)
            throw new FiscalConfigurationValidationException("La sede es obligatoria.");
        if (string.IsNullOrWhiteSpace(request.AuthorizationNumber) ||
            string.IsNullOrWhiteSpace(request.SupplierTaxId) ||
            string.IsNullOrWhiteSpace(request.Prefix) ||
            string.IsNullOrWhiteSpace(request.QrValidationUrl) ||
            string.IsNullOrWhiteSpace(request.TechnicalKeyVersion))
            throw new FiscalConfigurationValidationException(
                "Número de resolución, NIT, prefijo, URL QR y versión de clave técnica son obligatorios.");
        if (request.Environment is not (1 or 2))
            throw new FiscalConfigurationValidationException("El ambiente fiscal no es válido.");
        if (request.ValidUntil < request.ValidFrom)
            throw new FiscalConfigurationValidationException("La vigencia final no puede ser anterior a la inicial.");
        if (request.RangeStart < 1 || request.RangeEnd < request.RangeStart)
            throw new FiscalConfigurationValidationException("El rango autorizado no es válido.");
        var configuredNumbering = await numbering.GetAsync(
            user.TenantId, businessId, cancellationToken);
        if (configuredNumbering.InitialConsecutive is not long initialConsecutive)
            throw new FiscalConfigurationValidationException(
                "Configura primero el consecutivo inicial de facturación.");
        if (initialConsecutive < request.RangeStart || initialConsecutive > request.RangeEnd)
            throw new FiscalConfigurationValidationException(
                "El consecutivo inicial debe estar dentro del rango autorizado por la DIAN.");
        if (request.PrepareOnlineSeries && request.PrepareOfflineSeries && request.RangeEnd == initialConsecutive)
            throw new FiscalConfigurationValidationException(
                "El rango necesita al menos dos consecutivos para preparar ambos modos.");
        if (!request.PrepareOnlineSeries && !request.PrepareOfflineSeries)
            throw new FiscalConfigurationValidationException(
                "Selecciona al menos un modo de facturación.");
        return await store.SaveAsync(
            user.TenantId,
            businessId,
            request with { InitialConsecutive = initialConsecutive },
            cancellationToken);
    }

    private static void Demand(FiscalConfigurationUser user, string permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(permission))
            throw new FiscalConfigurationForbiddenException($"Permission '{permission}' is required.");
    }
}
