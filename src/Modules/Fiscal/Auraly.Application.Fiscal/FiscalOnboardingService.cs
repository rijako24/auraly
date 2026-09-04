using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Auraly.BuildingBlocks.Domain.Identity;
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
        string supplierCheckDigit,
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
    IDianNumberingRangeClient numberingRanges,
    TimeProvider timeProvider)
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
            request.CertificatePassword,
            current.SupplierTaxId,
            current.SupplierCheckDigit,
            timeProvider.GetUtcNow());
        var supplierCheckDigit = ResolveSupplierCheckDigit(current);
        var stored = await credentials.StoreAsync(
            user.TenantId,
            businessId,
            request.SoftwarePin.Trim(),
            certificate.NormalizedPfx,
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
            supplierCheckDigit,
            stored,
            cancellationToken);
        return await store.GetAsync(user.TenantId, businessId, cancellationToken);
    }

    private static string ResolveSupplierCheckDigit(FiscalOnboardingConfiguration current)
    {
        if (!string.IsNullOrWhiteSpace(current.SupplierCheckDigit))
            return current.SupplierCheckDigit.Trim();
        if (ColombianNit.TryCalculateVerificationDigit(
                current.SupplierTaxId, out var verificationDigit))
            return verificationDigit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        throw new FiscalConfigurationValidationException(
            "El perfil legal no contiene un NIT válido para calcular el dígito de verificación.");
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
        string password,
        string supplierTaxId,
        string supplierCheckDigit,
        DateTimeOffset now)
    {
        X509Certificate2Collection collection;
        try
        {
            collection = FiscalPkcs12Importer.Import(pfx, password);
        }
        catch (CryptographicException)
        {
            throw new FiscalConfigurationValidationException(
                "No fue posible abrir el PFX/P12: la contraseña es incorrecta o el archivo está dañado.");
        }

        try
        {
            using var certificate = collection.OfType<X509Certificate2>()
                .SingleOrDefault(item => item.HasPrivateKey)
                ?? throw new FiscalConfigurationValidationException(
                    "El certificado debe contener exactamente una clave privada.");
            if (collection.OfType<X509Certificate2>().Count(item => item.HasPrivateKey) != 1)
                throw new FiscalConfigurationValidationException(
                    "El archivo debe contener exactamente un certificado con clave privada.");
            if (now < certificate.NotBefore)
                throw new FiscalConfigurationValidationException(
                    $"El certificado todavía no está vigente; inicia el {certificate.NotBefore:yyyy-MM-dd HH:mm} UTC.");
            if (now > certificate.NotAfter)
                throw new FiscalConfigurationValidationException(
                    $"El certificado está vencido desde el {certificate.NotAfter:yyyy-MM-dd HH:mm} UTC.");
            if (!FiscalCertificateIdentityPolicy.IsAcceptable(
                    supplierTaxId, supplierCheckDigit, certificate.Subject))
                throw new FiscalConfigurationValidationException(
                    "El NIT del certificado no coincide con el NIT del perfil legal.");

            // Product policy: validate the credential and its legal owner, but do not
            // gate onboarding by issuer, trust chain or declared key-usage extensions.
            var probe = RandomNumberGenerator.GetBytes(32);
            if (certificate.GetRSAPrivateKey() is RSA rsa)
                _ = rsa.SignHash(probe, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            else if (certificate.GetECDsaPrivateKey() is ECDsa ecdsa)
                _ = ecdsa.SignHash(probe);
            else
                throw new FiscalConfigurationValidationException(
                    "La clave privada del certificado no usa un algoritmo de firma compatible.");

            var normalizedPfx = collection.Export(X509ContentType.Pkcs12, password)
                ?? throw new FiscalConfigurationValidationException(
                    "No fue posible normalizar el certificado para almacenarlo de forma segura.");
            return new ValidatedCertificate(
                certificate.Thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
                certificate.NotBefore,
                certificate.NotAfter,
                normalizedPfx);
        }
        finally
        {
            collection.DisposeAll();
        }
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
        DateTimeOffset NotAfter,
        byte[] NormalizedPfx);
}

public static class FiscalCertificateIdentityPolicy
{
    private static readonly Regex SubjectSerialNumber = new(
        """(?:^|[,;+])\s*(?:SERIALNUMBER|OID\.2\.5\.4\.5|2\.5\.4\.5)\s*=\s*(?<value>"[^"]*"|[^,;+]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsAcceptable(
        string supplierTaxId,
        string supplierCheckDigit,
        string certificateSubject)
    {
        var expected = Digits(supplierTaxId);
        if (expected.Length == 0 || string.IsNullOrWhiteSpace(certificateSubject)) return false;
        var checkDigit = Digits(supplierCheckDigit);
        var accepted = checkDigit.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal) { expected }
            : new HashSet<string>(StringComparer.Ordinal) { expected, expected + checkDigit };

        return SubjectSerialNumber.Matches(certificateSubject)
            .Select(match => Digits(match.Groups["value"].Value))
            .Any(accepted.Contains);
    }

    private static string Digits(string value) =>
        new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}
