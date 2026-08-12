using System.Runtime.Versioning;
using Microsoft.AspNetCore.DataProtection;

namespace Auraly.Pos.Edge.Host;

public static class PosEdgeProtectedSecret
{
    private const string ApplicationName = "Auraly.Pos.Edge";
    private const string TechnicalKeyPurpose = "Auraly.Fiscal.TechnicalKey.v1";
    private const string EnrollmentPurpose = "Auraly.Pos.Edge.Enrollment.v1";
    private const string IdentityVerifierPurpose = "Auraly.Pos.Edge.IdentityVerifier.v1";

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

    public static string ProtectEnrollmentPackage(string keyDirectory, string packageJson)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
            throw new ArgumentException("An enrollment package is required.", nameof(packageJson));
        return CreateProtector(keyDirectory, EnrollmentPurpose).Protect(packageJson);
    }

    public static string UnprotectEnrollmentPackage(
        string keyDirectory,
        string protectedPackage)
    {
        if (string.IsNullOrWhiteSpace(protectedPackage))
            throw new ArgumentException(
                "A protected enrollment package is required.", nameof(protectedPackage));
        try
        {
            return CreateProtector(keyDirectory, EnrollmentPurpose).Unprotect(protectedPackage);
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            throw new InvalidOperationException(
                "The POS enrollment belongs to another Windows user or machine.",
                exception);
        }
    }

    public static string ProtectIdentityVerifier(
        string keyDirectory,
        string verifierJson)
    {
        if (string.IsNullOrWhiteSpace(verifierJson))
            throw new ArgumentException(
                "An identity verifier is required.", nameof(verifierJson));
        return CreateProtector(keyDirectory, IdentityVerifierPurpose)
            .Protect(verifierJson);
    }

    public static string UnprotectIdentityVerifier(
        string keyDirectory,
        string protectedVerifier)
    {
        if (string.IsNullOrWhiteSpace(protectedVerifier))
            throw new ArgumentException(
                "A protected identity verifier is required.",
                nameof(protectedVerifier));
        try
        {
            return CreateProtector(keyDirectory, IdentityVerifierPurpose)
                .Unprotect(protectedVerifier);
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            throw new InvalidOperationException(
                "The POS identity verifier belongs to another Windows user or machine.",
                exception);
        }
    }

    private static IDataProtector CreateProtector(string keyDirectory)
        => CreateProtector(keyDirectory, TechnicalKeyPurpose);

    private static IDataProtector CreateProtector(string keyDirectory, string purpose)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "POS Edge fiscal secret protection currently requires Windows DPAPI.");
        if (string.IsNullOrWhiteSpace(keyDirectory))
            throw new ArgumentException("A secret key directory is required.", nameof(keyDirectory));

        var directory = new DirectoryInfo(Path.GetFullPath(keyDirectory));
        directory.Create();
        return CreateWindowsProtector(directory, purpose);
    }

    [SupportedOSPlatform("windows")]
    private static IDataProtector CreateWindowsProtector(DirectoryInfo directory, string purpose)
    {
        var provider = DataProtectionProvider.Create(
            directory,
            configuration => configuration
                .SetApplicationName(ApplicationName)
                .ProtectKeysWithDpapi());
        return provider.CreateProtector(purpose);
    }
}
