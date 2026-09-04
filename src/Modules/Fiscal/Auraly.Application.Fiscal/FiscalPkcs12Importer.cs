using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace Auraly.Application.Fiscal;

internal static class FiscalPkcs12Importer
{
    private const X509KeyStorageFlags ImportFlags =
        X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable;

    public static X509Certificate2Collection Import(byte[] pfx, string password)
    {
        var certificates = new X509Certificate2Collection();
        try
        {
            certificates.Import(pfx, password, ImportFlags);
            return certificates;
        }
        catch (CryptographicException nativeException)
        {
            certificates.DisposeAll();
            try
            {
                return ImportLegacy(pfx, password);
            }
            catch (Exception legacyException) when (
                legacyException is IOException or CryptographicException or PkcsException)
            {
                throw new CryptographicException(
                    "The PKCS#12 payload could not be opened with the supplied password.",
                    new AggregateException(nativeException, legacyException));
            }
        }
    }

    private static X509Certificate2Collection ImportLegacy(byte[] pfx, string password)
    {
        var store = new Pkcs12StoreBuilder().Build();
        using var stream = new MemoryStream(pfx, writable: false);
        store.Load(stream, password.ToCharArray());

        var result = new X509Certificate2Collection();
        foreach (string alias in store.Aliases)
        {
            if (!store.IsKeyEntry(alias)) continue;
            var key = store.GetKey(alias)?.Key;
            var chain = store.GetCertificateChain(alias);
            if (key is null || chain is null || chain.Length == 0) continue;

            using var publicCertificate = new X509Certificate2(chain[0].Certificate.GetEncoded());
            var certificateWithKey = AttachPrivateKey(publicCertificate, key);
            result.Add(certificateWithKey);
            foreach (var chainEntry in chain.Skip(1))
                result.Add(new X509Certificate2(chainEntry.Certificate.GetEncoded()));
        }

        if (result.Count == 0)
            throw new CryptographicException("The PKCS#12 payload contains no private key.");
        return result;
    }

    private static X509Certificate2 AttachPrivateKey(
        X509Certificate2 certificate,
        Org.BouncyCastle.Crypto.AsymmetricKeyParameter key)
    {
        if (key is RsaPrivateCrtKeyParameters rsaKey)
        {
            using var rsa = RSA.Create();
            var pkcs8 = PrivateKeyInfoFactory.CreatePrivateKeyInfo(rsaKey).GetEncoded();
            rsa.ImportPkcs8PrivateKey(pkcs8, out _);
            return certificate.CopyWithPrivateKey(rsa);
        }
        if (key is ECPrivateKeyParameters ecKey)
        {
            using var ecdsa = ECDsa.Create();
            var pkcs8 = PrivateKeyInfoFactory.CreatePrivateKeyInfo(ecKey).GetEncoded();
            ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
            return certificate.CopyWithPrivateKey(ecdsa);
        }
        throw new CryptographicException(
            "The PKCS#12 private key algorithm is not supported.");
    }

    public static void DisposeAll(this X509Certificate2Collection certificates)
    {
        foreach (var certificate in certificates)
            certificate.Dispose();
    }
}
