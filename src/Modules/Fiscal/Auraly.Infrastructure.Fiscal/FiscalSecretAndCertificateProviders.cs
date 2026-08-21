using System.Security.Cryptography.X509Certificates;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;

namespace Auraly.Infrastructure.Fiscal;

public sealed class EnvironmentFiscalSoftwarePinProvider : IFiscalSoftwarePinProvider
{
    public Task<string> ResolveAsync(Guid businessId, string secretReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (businessId == Guid.Empty)
            throw new ArgumentException("BusinessId is required.", nameof(businessId));
        const string prefix = "env://";
        if (!secretReference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The software PIN reference must use env:// in this deployment.");
        var variable = secretReference[prefix.Length..].Trim();
        if (variable.Length == 0 || variable.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            throw new InvalidOperationException("The software PIN environment reference is invalid.");
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("The referenced software PIN secret is unavailable.");
        return Task.FromResult(value);
    }
}

public sealed class WindowsFiscalSigningCertificateProvider : IFiscalSigningCertificateProvider
{
    public Task<FiscalCertificateMaterial> ResolveAsync(FiscalCertificateReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(reference.Provider, "WindowsCertificateStore",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This deployment only supports WindowsCertificateStore certificates.");
        var parts = reference.KeyReference.Split('/', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !Enum.TryParse<StoreLocation>(parts[0], true, out var location) ||
            !Enum.TryParse<StoreName>(parts[1], true, out var name))
            throw new InvalidOperationException("CertificateKeyReference must be StoreLocation/StoreName.");
        using var store = new X509Store(name, location);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var thumbprint = Normalize(reference.ExpectedThumbprint);
        var certificate = store.Certificates.OfType<X509Certificate2>()
            .SingleOrDefault(item => Normalize(item.Thumbprint) == thumbprint)
            ?? throw new InvalidOperationException("The configured fiscal certificate is unavailable.");
        return Task.FromResult(new FiscalCertificateMaterial(certificate, [], RequireTrustedChain: true));
    }

    private static string Normalize(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}

public sealed class ManagedFiscalSoftwarePinProvider(
    IFiscalCredentialVault credentials,
    EnvironmentFiscalSoftwarePinProvider legacy) : IFiscalSoftwarePinProvider
{
    public Task<string> ResolveAsync(
        Guid businessId, string secretReference, CancellationToken cancellationToken) =>
        secretReference.StartsWith("env://", StringComparison.OrdinalIgnoreCase)
            ? legacy.ResolveAsync(businessId, secretReference, cancellationToken)
            : credentials.ResolveSoftwarePinAsync(businessId, secretReference, cancellationToken);
}

public sealed class ManagedFiscalSigningCertificateProvider(
    IFiscalCredentialVault credentials,
    WindowsFiscalSigningCertificateProvider legacy) : IFiscalSigningCertificateProvider
{
    public async Task<FiscalCertificateMaterial> ResolveAsync(
        FiscalCertificateReference reference,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(reference.Provider, "WindowsCertificateStore", StringComparison.OrdinalIgnoreCase))
            return await legacy.ResolveAsync(reference, cancellationToken);
        if (reference.Provider is not ("AzureKeyVault" or "ProtectedDatabase"))
            throw new InvalidOperationException("The configured fiscal certificate provider is unsupported.");
        var pfx = await credentials.ResolveCertificatePfxAsync(
            reference.BusinessId, reference.KeyReference, cancellationToken);
        var collection = new X509Certificate2Collection();
        collection.Import(pfx, null,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        var certificate = collection.OfType<X509Certificate2>().Single(item => item.HasPrivateKey);
        var thumbprint = certificate.Thumbprint
            .Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (!string.Equals(thumbprint,
                reference.ExpectedThumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
                StringComparison.Ordinal))
            throw new InvalidOperationException("The configured fiscal certificate thumbprint does not match the stored certificate.");
        var chain = collection.OfType<X509Certificate2>()
            .Where(item => item.Thumbprint != certificate.Thumbprint)
            .ToArray();
        return new FiscalCertificateMaterial(certificate, chain, RequireTrustedChain: true);
    }
}
