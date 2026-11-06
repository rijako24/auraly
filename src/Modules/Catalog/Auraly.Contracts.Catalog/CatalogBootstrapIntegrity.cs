using System.Security.Cryptography;
using System.Text.Json;

namespace Auraly.Contracts.Catalog;

/// <summary>
/// Protects the complete current product contract in a catalog bootstrap page.
/// </summary>
public static class CatalogBootstrapIntegrity
{
    public static string Compute(IEnumerable<PosCatalogItem> items)
    {
        return Convert.ToHexString(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(items)))
            .ToLowerInvariant();
    }

    public static bool IsValid(IEnumerable<PosCatalogItem> items, string integrityHash)
    {
        if (string.IsNullOrWhiteSpace(integrityHash))
            return false;
        try
        {
            var expected = Convert.FromHexString(Compute(items));
            var received = Convert.FromHexString(integrityHash);
            return expected.Length == received.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, received);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
