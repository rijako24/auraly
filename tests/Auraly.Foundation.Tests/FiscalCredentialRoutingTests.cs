using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Auraly.Infrastructure.Fiscal;

namespace Auraly.Foundation.Tests;

public sealed class FiscalCredentialRoutingTests
{
    [Fact]
    public async Task Historical_database_references_resolve_without_changing_new_azure_writes()
    {
        var azure = new RecordingVault("AzureKeyVault");
        var database = new RecordingVault("ProtectedDatabase");
        var routed = new RoutedFiscalCredentialVault(azure, database);
        var businessId = Guid.NewGuid();

        Assert.Equal("ProtectedDatabase", await routed.ResolveSoftwarePinAsync(
            businessId, $"fiscal://tenant/{Guid.NewGuid():N}", CancellationToken.None));
        Assert.Equal([2], await routed.ResolveCertificatePfxAsync(
            businessId, $"fiscal://tenant/{Guid.NewGuid():N}", CancellationToken.None));
        Assert.Equal("AzureKeyVault", await routed.ResolveSoftwarePinAsync(
            businessId, $"akv-secret://dian-tenant-{Guid.NewGuid():N}-pin", CancellationToken.None));
        Assert.Equal([1], await routed.ResolveCertificatePfxAsync(
            businessId, $"akv-certificate://dian-tenant-{Guid.NewGuid():N}", CancellationToken.None));
        await routed.StoreAsync(Guid.NewGuid(), businessId, "pin", [1], "password",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), "thumbprint", CancellationToken.None);
        await routed.StoreSupportDocumentSoftwarePinAsync(
            Guid.NewGuid(), businessId, "pin", CancellationToken.None);

        Assert.Equal(4, azure.Calls);
        Assert.Equal(2, database.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => routed.ResolveSoftwarePinAsync(
            businessId, "unknown://reference", CancellationToken.None));
    }

    private sealed class RecordingVault(string provider) : IFiscalCredentialVault
    {
        public int Calls { get; private set; }

        public Task<FiscalCredentialReference> StoreAsync(
            Guid tenantId, Guid businessId, string softwarePin, byte[] certificatePfx,
            string certificatePassword, DateTimeOffset validFrom, DateTimeOffset validTo,
            string thumbprint, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new FiscalCredentialReference(
                provider, "reference", "reference", thumbprint, validFrom, validTo));
        }

        public Task<string> StoreSupportDocumentSoftwarePinAsync(
            Guid tenantId, Guid businessId, string softwarePin, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(provider);
        }

        public Task<string> ResolveSoftwarePinAsync(
            Guid businessId, string secretReference, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(provider);
        }

        public Task<byte[]> ResolveCertificatePfxAsync(
            Guid businessId, string certificateKeyReference, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new[] { provider == "AzureKeyVault" ? (byte)1 : (byte)2 });
        }
    }
}
