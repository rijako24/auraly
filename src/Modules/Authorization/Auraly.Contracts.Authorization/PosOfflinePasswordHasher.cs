using System.Security.Cryptography;

namespace Auraly.Contracts.Authorization;

public static class PosOfflinePasswordHasher
{
    public const int DefaultIterations = 210_000;
    public const int SaltSize = 16;
    public const int HashSize = 32;

    public static PosOfflinePasswordVerifier Hash(
        string password,
        DateTimeOffset changedAt,
        int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (iterations < 100_000)
            throw new ArgumentOutOfRangeException(nameof(iterations));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashSize);
        return new PosOfflinePasswordVerifier(salt, hash, iterations, changedAt);
    }

    public static bool Verify(string password, PosOfflinePasswordVerifier verifier)
    {
        if (string.IsNullOrEmpty(password) ||
            verifier.Salt.Length != SaltSize ||
            verifier.Hash.Length != HashSize ||
            verifier.Iterations < 100_000)
            return false;

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            password,
            verifier.Salt,
            verifier.Iterations,
            HashAlgorithmName.SHA256,
            verifier.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(candidate, verifier.Hash);
    }
}
