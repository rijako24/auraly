using System.Runtime.Versioning;
using Microsoft.AspNetCore.DataProtection;

namespace Auraly.Pos.Edge.Host;

public static class PosEdgeProtectedSecret
{
    private const string ApplicationName = "Auraly.Pos.Edge";
    private const string TechnicalKeyPurpose = "Auraly.Fiscal.TechnicalKey.v1";

    public static string ProtectTechnicalKey(string keyDirectory, string technicalKey)
    {
        if (string.IsNullOrWhiteSpace(technicalKey))
            throw new ArgumentException("A technical key is required.", nameof(technicalKey));
        return CreateProtector(keyDirectory).Protect(technicalKey);
    }

    public static string UnprotectTechnicalKey(string keyDirectory, string protectedTechnicalKey)
    {
        if (string.IsNullOrWhiteSpace(protectedTechnicalKey))
            throw new ArgumentException("A protected technical key is required.", nameof(protectedTechnicalKey));
        try
        {
            return CreateProtector(keyDirectory).Unprotect(protectedTechnicalKey);
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            throw new InvalidOperationException(
                "The fiscal technical key cannot be decrypted for the current Windows user.",
                exception);
        }
    }

    private static IDataProtector CreateProtector(string keyDirectory)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "POS Edge fiscal secret protection currently requires Windows DPAPI.");
        if (string.IsNullOrWhiteSpace(keyDirectory))
            throw new ArgumentException("A secret key directory is required.", nameof(keyDirectory));

        var directory = new DirectoryInfo(Path.GetFullPath(keyDirectory));
        directory.Create();
        return CreateWindowsProtector(directory);
    }

    [SupportedOSPlatform("windows")]
    private static IDataProtector CreateWindowsProtector(DirectoryInfo directory)
    {
        var provider = DataProtectionProvider.Create(
            directory,
            configuration => configuration
                .SetApplicationName(ApplicationName)
                .ProtectKeysWithDpapi());
        return provider.CreateProtector(TechnicalKeyPurpose);
    }
}
