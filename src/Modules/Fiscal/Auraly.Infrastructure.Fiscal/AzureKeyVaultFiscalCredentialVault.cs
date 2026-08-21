using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;

namespace Auraly.Infrastructure.Fiscal;

public sealed class AzureKeyVaultFiscalCredentialVault(
    CertificateClient certificates,
    SecretClient secrets) : IFiscalCredentialVault
{
    public static AzureKeyVaultFiscalCredentialVault Create(Uri vaultUri)
    {
        var credential = new DefaultAzureCredential();
        return new AzureKeyVaultFiscalCredentialVault(
            new CertificateClient(vaultUri, credential),
            new SecretClient(vaultUri, credential));
    }

    public async Task<FiscalCredentialReference> StoreAsync(
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
        var certificateName = CertificateName(businessId);
        var pinName = PinName(businessId);
        var options = new ImportCertificateOptions(certificateName, certificatePfx)
        {
            Password = certificatePassword,
            Enabled = true,
            Tags =
            {
                ["tenant-id"] = tenantId.ToString("N"),
                ["business-id"] = businessId.ToString("N"),
                ["purpose"] = "dian-signing"
            }
        };
        await certificates.ImportCertificateAsync(options, cancellationToken);
        await secrets.SetSecretAsync(
            new KeyVaultSecret(pinName, softwarePin)
            {
                Properties =
                {
                    Tags =
                    {
                        ["tenant-id"] = tenantId.ToString("N"),
                        ["business-id"] = businessId.ToString("N"),
                        ["purpose"] = "dian-software-pin"
                    }
                }
            },
            cancellationToken);
        return new FiscalCredentialReference(
            "AzureKeyVault",
            $"akv-secret://{pinName}",
            $"akv-certificate://{certificateName}",
            thumbprint,
            validFrom,
            validTo);
    }

    public async Task<string> ResolveSoftwarePinAsync(
        Guid businessId, string secretReference, CancellationToken cancellationToken)
    {
        var expected = $"akv-secret://{PinName(businessId)}";
        if (!string.Equals(secretReference, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Azure Key Vault PIN reference is invalid for this business.");
        var response = await secrets.GetSecretAsync(PinName(businessId), cancellationToken: cancellationToken);
        return response.Value.Value;
    }

    public async Task<byte[]> ResolveCertificatePfxAsync(
        Guid businessId, string certificateKeyReference, CancellationToken cancellationToken)
    {
        var expected = $"akv-certificate://{CertificateName(businessId)}";
        if (!string.Equals(certificateKeyReference, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Azure Key Vault certificate reference is invalid for this business.");
        var response = await secrets.GetSecretAsync(
            CertificateName(businessId), cancellationToken: cancellationToken);
        try
        {
            return Convert.FromBase64String(response.Value.Value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "The Azure Key Vault certificate secret is not a PKCS#12 payload.", exception);
        }
    }

    private static string CertificateName(Guid businessId) => $"dian-{businessId:N}";
    private static string PinName(Guid businessId) => $"dian-{businessId:N}-pin";
}
