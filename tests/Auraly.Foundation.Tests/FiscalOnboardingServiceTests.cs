using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace Auraly.Foundation.Tests;

public sealed class FiscalOnboardingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Loading_an_untrusted_self_signed_certificate_is_accepted()
    {
        const string password = "certificate-password";
        const string supplierTaxId = "100226966";
        var store = new TestOnboardingStore(Configuration(supplierTaxId));
        var vault = new TestCredentialVault();
        var service = new FiscalOnboardingService(
            store, vault, new TestNumberingRangeClient(), new FixedTimeProvider(Now));
        var request = new SaveDianHabilitationConfiguration(
            Guid.NewGuid().ToString(),
            "software-pin",
            Guid.NewGuid(),
            password,
            CreatePfx(supplierTaxId + "8", password));
        var user = new FiscalConfigurationUser(
            Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { FiscalPermissionCodes.ConfigurationManage });

        await service.ConfigureHabilitationAsync(
            user, store.Configuration.BusinessId, request);

        Assert.True(vault.StoreCalled);
        Assert.True(store.SaveCalled);
    }

    [Fact]
    public async Task Loading_a_legacy_rc2_pkcs12_is_accepted_and_normalized()
    {
        const string password = "legacy-certificate-password";
        const string supplierTaxId = "100226966";
        var store = new TestOnboardingStore(Configuration(supplierTaxId));
        var vault = new TestCredentialVault();
        var service = new FiscalOnboardingService(
            store, vault, new TestNumberingRangeClient(), new FixedTimeProvider(Now));
        var modernPfx = CreatePfx(supplierTaxId + "8", password);
        var request = new SaveDianHabilitationConfiguration(
            Guid.NewGuid().ToString(), "software-pin", Guid.NewGuid(), password,
            ReencodeWithLegacyRc2(modernPfx, password));
        var user = new FiscalConfigurationUser(
            Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { FiscalPermissionCodes.ConfigurationManage });

        await service.ConfigureHabilitationAsync(
            user, store.Configuration.BusinessId, request);

        Assert.NotNull(vault.StoredCertificatePfx);
        var normalized = new X509Certificate2Collection();
        normalized.Import(
            vault.StoredCertificatePfx!, password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        Assert.Single(normalized.OfType<X509Certificate2>().Where(value => value.HasPrivateKey));
        foreach (var certificate in normalized) certificate.Dispose();
    }

    [Fact]
    public async Task Loading_a_certificate_with_another_nit_is_rejected()
    {
        const string password = "certificate-password";
        var store = new TestOnboardingStore(Configuration("100226966"));
        var vault = new TestCredentialVault();
        var service = new FiscalOnboardingService(
            store, vault, new TestNumberingRangeClient(), new FixedTimeProvider(Now));
        var request = new SaveDianHabilitationConfiguration(
            Guid.NewGuid().ToString(),
            "software-pin",
            Guid.NewGuid(),
            password,
            CreatePfx("49693606", password));
        var user = new FiscalConfigurationUser(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new HashSet<string> { FiscalPermissionCodes.ConfigurationManage });

        var exception = await Assert.ThrowsAsync<FiscalConfigurationValidationException>(() =>
            service.ConfigureHabilitationAsync(
                user, store.Configuration.BusinessId, request));

        Assert.Contains("NIT del certificado", exception.Message);
        Assert.False(vault.StoreCalled);
        Assert.False(store.SaveCalled);
    }

    [Fact]
    public async Task Loading_a_certificate_with_the_wrong_password_reports_the_credential_error()
    {
        const string supplierTaxId = "100226966";
        var store = new TestOnboardingStore(Configuration(supplierTaxId));
        var vault = new TestCredentialVault();
        var service = new FiscalOnboardingService(
            store, vault, new TestNumberingRangeClient(), new FixedTimeProvider(Now));
        var request = new SaveDianHabilitationConfiguration(
            Guid.NewGuid().ToString(),
            "software-pin",
            Guid.NewGuid(),
            "wrong-password",
            CreatePfx(supplierTaxId + "8", "correct-password"));
        var user = new FiscalConfigurationUser(
            Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { FiscalPermissionCodes.ConfigurationManage });

        var exception = await Assert.ThrowsAsync<FiscalConfigurationValidationException>(() =>
            service.ConfigureHabilitationAsync(
                user, store.Configuration.BusinessId, request));

        Assert.Contains("contraseña es incorrecta", exception.Message);
        Assert.False(vault.StoreCalled);
        Assert.False(store.SaveCalled);
    }

    [Fact]
    public async Task Loading_an_expired_certificate_is_rejected()
    {
        const string password = "certificate-password";
        const string supplierTaxId = "100226966";
        var store = new TestOnboardingStore(Configuration(supplierTaxId));
        var vault = new TestCredentialVault();
        var service = new FiscalOnboardingService(
            store, vault, new TestNumberingRangeClient(), new FixedTimeProvider(Now));
        var request = new SaveDianHabilitationConfiguration(
            Guid.NewGuid().ToString(),
            "software-pin",
            Guid.NewGuid(),
            password,
            CreatePfx(supplierTaxId + "8", password, Now.AddDays(-3), Now.AddDays(-2)));
        var user = new FiscalConfigurationUser(
            Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { FiscalPermissionCodes.ConfigurationManage });

        var exception = await Assert.ThrowsAsync<FiscalConfigurationValidationException>(() =>
            service.ConfigureHabilitationAsync(
                user, store.Configuration.BusinessId, request));

        Assert.Contains("está vencido", exception.Message);
        Assert.False(vault.StoreCalled);
        Assert.False(store.SaveCalled);
    }

    [Fact]
    public async Task Support_document_range_requires_production_and_uses_the_onboarding_store()
    {
        var inactiveStore = new TestOnboardingStore(Configuration("100226966"));
        var user = new FiscalConfigurationUser(
            Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { FiscalPermissionCodes.ConfigurationManage });
        var inactiveService = new FiscalOnboardingService(
            inactiveStore, new TestCredentialVault(), new TestNumberingRangeClient(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<FiscalConfigurationValidationException>(() =>
            inactiveService.ActivateSupportDocumentAsync(
                user, inactiveStore.Configuration.BusinessId, Guid.NewGuid()));
        Assert.False(inactiveStore.SupportActivationCalled);

        var activeStore = new TestOnboardingStore(
            Configuration("100226966") with { ProductionActive = true });
        var activeService = new FiscalOnboardingService(
            activeStore, new TestCredentialVault(), new TestNumberingRangeClient(),
            new FixedTimeProvider(Now));

        await activeService.ActivateSupportDocumentAsync(
            user, activeStore.Configuration.BusinessId, Guid.NewGuid());

        Assert.True(activeStore.SupportActivationCalled);
    }

    private static byte[] CreatePfx(
        string certificateIdentity,
        string password,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN=Firmante fiscal, SERIALNUMBER={certificateIdentity}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature,
            critical: true));
        using var certificate = request.CreateSelfSigned(
            notBefore ?? Now.AddDays(-1),
            notAfter ?? Now.AddDays(1));
        return certificate.Export(X509ContentType.Pfx, password);
    }

    private static byte[] ReencodeWithLegacyRc2(byte[] pfx, string password)
    {
        var source = new Pkcs12StoreBuilder().Build();
        using (var input = new MemoryStream(pfx, writable: false))
            source.Load(input, password.ToCharArray());
        var legacy = new Pkcs12StoreBuilder()
            .SetCertAlgorithm(PkcsObjectIdentifiers.PbewithShaAnd40BitRC2Cbc)
            .Build();
        foreach (string alias in source.Aliases)
        {
            if (source.IsKeyEntry(alias))
                legacy.SetKeyEntry(alias, source.GetKey(alias), source.GetCertificateChain(alias));
            else if (source.IsCertificateEntry(alias))
                legacy.SetCertificateEntry(alias, source.GetCertificate(alias));
        }
        using var output = new MemoryStream();
        legacy.Save(output, password.ToCharArray(), new SecureRandom());
        return output.ToArray();
    }

    private static FiscalOnboardingConfiguration Configuration(string supplierTaxId) => new(
        Guid.NewGuid(),
        "Sede principal",
        "Empresa de prueba",
        supplierTaxId,
        "8",
        FiscalOnboardingStages.NotConfigured,
        null,
        null,
        false,
        null,
        null,
        null,
        false,
        null,
        false,
        null,
        [],
        [],
        null);

    private sealed class TestOnboardingStore(FiscalOnboardingConfiguration configuration)
        : IFiscalOnboardingStore
    {
        public FiscalOnboardingConfiguration Configuration { get; } = configuration;
        public bool SaveCalled { get; private set; }
        public bool SupportActivationCalled { get; private set; }

        public Task<FiscalOnboardingConfiguration> GetAsync(
            Guid tenantId, Guid businessId, CancellationToken cancellationToken) =>
            Task.FromResult(Configuration);

        public Task SaveHabilitationAsync(
            Guid tenantId,
            Guid businessId,
            Guid userId,
            string softwareIdentificationCode,
            Guid testSetId,
            FiscalCredentialReference credentials,
            CancellationToken cancellationToken)
        {
            SaveCalled = true;
            return Task.CompletedTask;
        }

        public Task<DianNumberingRangeContext> GetNumberingRangeContextAsync(
            Guid tenantId, Guid businessId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ImportNumberingRangesAsync(
            Guid tenantId,
            IReadOnlyList<ImportedDianNumberingRange> ranges,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AssignOnlineResolutionAsync(
            Guid tenantId,
            Guid businessId,
            Guid userId,
            Guid dianNumberingRangeId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ActivateProductionAsync(
            Guid tenantId,
            Guid businessId,
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ActivateSupportDocumentAsync(
            Guid tenantId,
            Guid businessId,
            Guid userId,
            Guid dianNumberingRangeId,
            CancellationToken cancellationToken)
        {
            SupportActivationCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCredentialVault : IFiscalCredentialVault
    {
        public bool StoreCalled { get; private set; }
        public byte[]? StoredCertificatePfx { get; private set; }

        public Task<FiscalCredentialReference> StoreAsync(
            Guid tenantId,
            Guid businessId,
            string softwarePin,
            byte[] certificatePfx,
            string certificatePassword,
            DateTimeOffset validFrom,
            DateTimeOffset validTo,
            string thumbprint,
            CancellationToken cancellationToken)
        {
            StoreCalled = true;
            StoredCertificatePfx = certificatePfx;
            return Task.FromResult(new FiscalCredentialReference(
                "ProtectedDatabase",
                "fiscal://test/pin",
                "fiscal://test/certificate",
                thumbprint,
                validFrom,
                validTo));
        }

        public Task<string> ResolveSoftwarePinAsync(
            Guid businessId, string secretReference, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]> ResolveCertificatePfxAsync(
            Guid businessId, string certificateKeyReference, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestNumberingRangeClient : IDianNumberingRangeClient
    {
        public Task<IReadOnlyList<ImportedDianNumberingRange>> GetAsync(
            DianNumberingRangeContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
