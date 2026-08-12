using Auraly.Contracts.Fiscal;

namespace Auraly.Application.Fiscal;

public interface IFiscalIssuerConnectionStore
{
    Task<FiscalIssuerConnectionConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken);

    Task<FiscalIssuerConnectionConfiguration> SaveAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        SaveFiscalIssuerConnectionConfiguration request,
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuerConnectionService(IFiscalIssuerConnectionStore store)
{
    public Task<FiscalIssuerConnectionConfiguration> GetAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationRead);
        ValidateBusiness(businessId);
        return store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    public Task<FiscalIssuerConnectionConfiguration> SaveAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        SaveFiscalIssuerConnectionConfiguration request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        Validate(request);
        return store.SaveAsync(user.TenantId, businessId, user.UserId, request, cancellationToken);
    }

    private static void Validate(SaveFiscalIssuerConnectionConfiguration request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var required = new[]
        {
            request.SupplierTaxId, request.SupplierCheckDigit, request.LegalName,
            request.TaxLevelCode, request.TaxSchemeId, request.TaxSchemeName,
            request.IdentificationTypeCode, request.AddressLine, request.CityCode,
            request.CityName, request.DepartmentCode, request.DepartmentName,
            request.SoftwareIdentificationCode, request.SoftwarePinSecretReference,
            request.CertificateProvider, request.CertificateKeyReference,
            request.CertificateThumbprint, request.DianEndpoint,
            request.TechnicalAnnexVersion, request.GeneratorVersion
        };
        if (required.Any(string.IsNullOrWhiteSpace))
            throw new FiscalConfigurationValidationException(
                "Completa todos los datos obligatorios del emisor y de la conexión DIAN.");
        if (request.Environment is not (1 or 2))
            throw new FiscalConfigurationValidationException("El ambiente fiscal no es válido.");
        if (request.Environment == 2 && request.TestSetId is null)
            throw new FiscalConfigurationValidationException(
                "El TestSetId es obligatorio en el ambiente de habilitación.");
        if (request.ValidTo is not null && request.ValidTo <= request.ValidFrom)
            throw new FiscalConfigurationValidationException(
                "La vigencia final del emisor debe ser posterior a la inicial.");
        if (!request.SoftwarePinSecretReference.StartsWith("env://", StringComparison.OrdinalIgnoreCase))
            throw new FiscalConfigurationValidationException(
                "El PIN del software debe configurarse mediante una referencia env://; el secreto no se guarda en la base de datos.");
        if (!string.Equals(request.CertificateProvider, "WindowsCertificateStore", StringComparison.OrdinalIgnoreCase))
            throw new FiscalConfigurationValidationException(
                "Este despliegue admite certificados fiscales desde Windows Certificate Store.");
        if (request.CertificateKeyReference is not ("CurrentUser/My" or "LocalMachine/My"))
            throw new FiscalConfigurationValidationException(
                "El almacén del certificado debe ser CurrentUser/My o LocalMachine/My.");
        var thumbprint = request.CertificateThumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (thumbprint.Length is not (40 or 64) || thumbprint.Any(character => !Uri.IsHexDigit(character)))
            throw new FiscalConfigurationValidationException("La huella del certificado no es válida.");
        if (!Uri.TryCreate(request.DianEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            throw new FiscalConfigurationValidationException("El endpoint DIAN debe ser una URL HTTPS válida.");
        var expectedHost = request.Environment == 2 ? "vpfe-hab.dian.gov.co" : "vpfe.dian.gov.co";
        if (!string.Equals(endpoint.Host, expectedHost, StringComparison.OrdinalIgnoreCase))
            throw new FiscalConfigurationValidationException(
                $"El endpoint no corresponde al ambiente seleccionado ({expectedHost}).");
        if (!string.Equals(request.TechnicalAnnexVersion.Trim(), "1.9", StringComparison.Ordinal))
            throw new FiscalConfigurationValidationException(
                "Auraly genera actualmente el Anexo Técnico DIAN 1.9.");
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
            throw new FiscalConfigurationForbiddenException($"Permission '{permission}' is required.");
    }
}
