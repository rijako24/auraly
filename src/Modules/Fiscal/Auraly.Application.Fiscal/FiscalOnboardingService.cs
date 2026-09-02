using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Auraly.Contracts.Fiscal;

namespace Auraly.Application.Fiscal;

public interface IFiscalOnboardingStore
{
    Task<FiscalOnboardingConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken);

    Task SaveHabilitationAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        string softwareIdentificationCode,
        Guid testSetId,
        FiscalCredentialReference credentials,
        CancellationToken cancellationToken);

    Task<DianNumberingRangeContext> GetNumberingRangeContextAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken);

    Task ImportNumberingRangesAsync(
        Guid tenantId,
        IReadOnlyList<ImportedDianNumberingRange> ranges,
        CancellationToken cancellationToken);

    Task AssignOnlineResolutionAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        Guid dianNumberingRangeId,
        CancellationToken cancellationToken);

    Task ActivateProductionAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken);

    Task ActivateSupportDocumentAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        Guid dianNumberingRangeId,
        CancellationToken cancellationToken);
}

public interface IFiscalCredentialVault
{
    Task<FiscalCredentialReference> StoreAsync(
        Guid tenantId,
        Guid businessId,
        string softwarePin,
        byte[] certificatePfx,
        string certificatePassword,
        DateTimeOffset validFrom,
        DateTimeOffset validTo,
        string thumbprint,
        CancellationToken cancellationToken);

    Task<string> ResolveSoftwarePinAsync(
        Guid businessId, string secretReference, CancellationToken cancellationToken);

    Task<byte[]> ResolveCertificatePfxAsync(
        Guid businessId, string certificateKeyReference, CancellationToken cancellationToken);
}

public interface IDianNumberingRangeClient
{
    Task<IReadOnlyList<ImportedDianNumberingRange>> GetAsync(
        DianNumberingRangeContext context,
        CancellationToken cancellationToken);
}

public sealed class FiscalOnboardingService(
    IFiscalOnboardingStore store,
    IFiscalCredentialVault credentials,
    IDianNumberingRangeClient numberingRanges)
{
    private const int MaximumCertificateBytes = 2 * 1024 * 1024;

    public Task<FiscalOnboardingConfiguration> GetAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationRead);
        ValidateBusiness(businessId);
        return store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    public async Task<FiscalOnboardingConfiguration> ConfigureHabilitationAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        SaveDianHabilitationConfiguration request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        ValidateRequest(request);

        var current = await store.GetAsync(user.TenantId, businessId, cancellationToken);
        if (current.ProductionActive)
            throw new FiscalConfigurationValidationException(
                "La configuración de habilitación no puede reemplazarse después de activar producción.");

        var certificate = ValidateCertificate(
            request.CertificatePfx,
            request.CertificatePassword);
        var stored = await credentials.StoreAsync(
            user.TenantId,
            businessId,
            request.SoftwarePin.Trim(),
            request.CertificatePfx,
            request.CertificatePassword,
            certificate.NotBefore,
            certificate.NotAfter,
            certificate.Thumbprint,
            cancellationToken);
        await store.SaveHabilitationAsync(
            user.TenantId,
            businessId,
            user.UserId,
            request.SoftwareIdentificationCode.Trim(),
            request.TestSetId,
            stored,
            cancellationToken);
        return await store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    public async Task<FiscalOnboardingConfiguration> SynchronizeNumberingRangesAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        var current = await store.GetAsync(user.TenantId, businessId, cancellationToken);
        if (!current.HabilitationAccepted)
            throw new FiscalConfigurationValidationException(
                "La DIAN debe aceptar primero el set de pruebas de habilitación.");
        var context = await store.GetNumberingRangeContextAsync(
            user.TenantId, businessId, cancellationToken);
        var ranges = await numberingRanges.GetAsync(context, cancellationToken);
        if (ranges.Count == 0)
            throw new FiscalConfigurationValidationException(
                "La DIAN no devolvió resoluciones asociadas al software. Verifica la asociación en el portal DIAN.");
        await store.ImportNumberingRangesAsync(user.TenantId, ranges, cancellationToken);
        return await store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    public async Task<FiscalOnboardingConfiguration> AssignOnlineResolutionAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        Guid dianNumberingRangeId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        if (dianNumberingRangeId == Guid.Empty)
            throw new FiscalConfigurationValidationException("Selecciona una resolución DIAN disponible.");
        await store.AssignOnlineResolutionAsync(
            user.TenantId, businessId, user.UserId, dianNumberingRangeId, cancellationToken);
        return await store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    public async Task<FiscalOnboardingConfiguration> ActivateProductionAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        var current = await store.GetAsync(user.TenantId, businessId, cancellationToken);
        if (!current.HabilitationAccepted)
            throw new FiscalConfigurationValidationException(
                "La DIAN debe aceptar primero el set de pruebas de habilitación.");
        await store.ActivateProductionAsync(
            user.TenantId, businessId, user.UserId, cancellationToken);
        return await store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    public async Task<FiscalOnboardingConfiguration> ActivateSupportDocumentAsync(
        FiscalConfigurationUser user,
        Guid businessId,
        Guid dianNumberingRangeId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.ConfigurationManage);
        ValidateBusiness(businessId);
        if (dianNumberingRangeId == Guid.Empty)
            throw new FiscalConfigurationValidationException(
                "Selecciona una resolución DIAN de documento soporte disponible.");
        var current = await store.GetAsync(user.TenantId, businessId, cancellationToken);
        if (!current.ProductionActive)
            throw new FiscalConfigurationValidationException(
                "Activa primero la facturación electrónica de producción para esta sede.");
        await store.ActivateSupportDocumentAsync(
            user.TenantId, businessId, user.UserId, dianNumberingRangeId, cancellationToken);
        return await store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    private static void ValidateRequest(SaveDianHabilitationConfiguration request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SoftwareIdentificationCode) ||
            string.IsNullOrWhiteSpace(request.SoftwarePin) ||
            string.IsNullOrWhiteSpace(request.CertificatePassword))
            throw new FiscalConfigurationValidationException(
                "Software ID, PIN y contraseña del certificado son obligatorios.");
        if (!Guid.TryParse(request.SoftwareIdentificationCode, out _))
            throw new FiscalConfigurationValidationException(
                "El Software ID debe ser el identificador UUID entregado por la DIAN.");
        if (request.TestSetId == Guid.Empty)
            throw new FiscalConfigurationValidationException("El TestSetId es obligatorio.");
        if (request.CertificatePfx.Length is 0 or > MaximumCertificateBytes)
            throw new FiscalConfigurationValidationException(
                "El certificado PFX/P12 debe pesar entre 1 byte y 2 MB.");
    }

    private static ValidatedCertificate ValidateCertificate(
        byte[] pfx,
        string password)
    {
        X509Certificate2Collection collection = [];
        try
        {
            collection.Import(
                pfx,
                password,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (CryptographicException)
        {
            throw new FiscalConfigurationValidationException(
                "El PFX/P12 o su contraseña no son válidos.");
        }

        using var certificate = collection.OfType<X509Certificate2>()
            .SingleOrDefault(item => item.HasPrivateKey)
            ?? throw new FiscalConfigurationValidationException(
                "El certificado debe contener exactamente una clave privada.");
        if (collection.OfType<X509Certificate2>().Count(item => item.HasPrivateKey) != 1)
            throw new FiscalConfigurationValidationException(
                "El archivo debe contener exactamente un certificado con clave privada.");
        // Product policy: onboarding accepts any parseable PFX/P12 containing one
        // usable signing key. DIAN remains the authority that accepts or rejects the
        // certificate for a fiscal submission; Auraly does not gate uploads by issuer,
        // chain, validity period, subject NIT or declared key-usage extensions.
        var probe = RandomNumberGenerator.GetBytes(32);
        if (certificate.GetRSAPrivateKey() is RSA rsa)
            _ = rsa.SignHash(probe, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        else if (certificate.GetECDsaPrivateKey() is ECDsa ecdsa)
            _ = ecdsa.SignHash(probe);
        else
            throw new FiscalConfigurationValidationException(
                "La clave privada del certificado no usa un algoritmo de firma compatible.");

        return new ValidatedCertificate(
            certificate.Thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            certificate.NotBefore,
            certificate.NotAfter);
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

    private sealed record ValidatedCertificate(
        string Thumbprint,
        DateTimeOffset NotBefore,
        DateTimeOffset NotAfter);
}
