using System.Security.Cryptography;
using System.Text;

namespace Auraly.Fiscal.Ubl;

public static class SoftwareSecurityCodeCalculator
{
    public static string Calculate(string softwareId, string softwarePin, string documentNumber)
    {
        if (string.IsNullOrWhiteSpace(softwareId)) throw new ArgumentException("Software ID is required.", nameof(softwareId));
        if (string.IsNullOrWhiteSpace(softwarePin)) throw new ArgumentException("Software PIN is required.", nameof(softwarePin));
        if (string.IsNullOrWhiteSpace(documentNumber)) throw new ArgumentException("Document number is required.", nameof(documentNumber));
        var bytes = Encoding.UTF8.GetBytes(softwareId.Trim() + softwarePin.Trim() + documentNumber.Trim());
        return Convert.ToHexString(SHA384.HashData(bytes)).ToLowerInvariant();
    }
}