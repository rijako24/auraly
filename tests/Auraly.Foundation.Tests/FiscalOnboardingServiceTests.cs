using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;

namespace Auraly.Foundation.Tests;

public sealed class FiscalOnboardingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Loading_a_certificate_with_another_nit_fails_before_persisting_credentials()
    {
        const string password = "certificate-password";
        var store = new TestOnboardingStore(Configuration("1002269668"));
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
            service.ConfigureHabilitationAsync(user, store.Configuration.BusinessId, request));

        Assert.Equal(
            "El NIT del certificado no coincide con el NIT del perfil legal.",
            exception.Message);
        Assert.False(vault.StoreCalled);
        Assert.False(store.SaveCalled);
    }

    private static byte[] CreatePfx(string certificateIdentity, string password)
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
        using var certificate = request.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(1));
        return certificate.Export(X509ContentType.Pfx, password);
    }

    private static FiscalOnboardingConfiguration Configuration(string supplierTaxId) => new(
        Guid.NewGuid(),
        "Sede principal",
        "Empresa de prueba",
        supplierTaxId,
        "0",
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
        []);

    private sealed class TestOnboardingStore(FiscalOnboardingConfiguration configuration)
        : IFiscalOnboardingStore
    {
        public FiscalOnboardingConfiguration Configuration { get; } = configuration;
        public bool SaveCalled { get; private set; }

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

        public Task ActivateProductionAsync(
            Guid tenantId,
            Guid businessId,
            Guid userId,
            Guid dianNumberingRangeId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestCredentialVault : IFiscalCredentialVault
    {
        public bool StoreCalled { get; private set; }

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
            throw new InvalidOperationException("Credentials must not be stored for an invalid certificate identity.");
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
