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
        var certificateName = CertificateName(tenantId);
        var pinName = PinName(tenantId);
        var options = new ImportCertificateOptions(certificateName, certificatePfx)
        {
            Password = certificatePassword,
            Enabled = true,
            Tags =
            {
                ["tenant-id"] = tenantId.ToString("N"),
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
        var pinName = ParseName(secretReference, "akv-secret://", "-pin");
        var response = await secrets.GetSecretAsync(pinName, cancellationToken: cancellationToken);
        return response.Value.Value;
    }

    public async Task<byte[]> ResolveCertificatePfxAsync(
        Guid businessId, string certificateKeyReference, CancellationToken cancellationToken)
    {
        var certificateName = ParseName(certificateKeyReference, "akv-certificate://", null);
        var response = await secrets.GetSecretAsync(
            certificateName, cancellationToken: cancellationToken);
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

    private static string CertificateName(Guid tenantId) => $"dian-tenant-{tenantId:N}";
    private static string PinName(Guid tenantId) => $"dian-tenant-{tenantId:N}-pin";

    private static string ParseName(string reference, string scheme, string? suffix)
    {
        if (!reference.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Azure Key Vault fiscal credential reference is invalid.");
        var name = reference[scheme.Length..];
        var idText = name.StartsWith("dian-tenant-", StringComparison.OrdinalIgnoreCase)
            ? name[12..]
            : string.Empty;
        if (suffix is not null && idText.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            idText = idText[..^suffix.Length];
        if (suffix is null && name.EndsWith("-pin", StringComparison.OrdinalIgnoreCase))
            idText = string.Empty;
        if (!Guid.TryParseExact(idText, "N", out _))
            throw new InvalidOperationException("The Azure Key Vault fiscal credential reference is invalid.");
        return name;
    }
}
