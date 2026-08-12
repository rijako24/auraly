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