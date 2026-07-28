using System.Security.Cryptography;

namespace Auraly.Infrastructure.Persistence;

public sealed record PosDeviceCredential(byte[] Salt, byte[] Hash, int Iterations);

public static class PosDeviceCredentialHasher
{
    public const int DefaultIterations = 120_000;

    public static PosDeviceCredential Create(string secret, int iterations = DefaultIterations)
    {
        Validate(secret, iterations);
        var salt = RandomNumberGenerator.GetBytes(32);
        return new PosDeviceCredential(salt, Derive(secret, salt, iterations), iterations);
    }

    public static bool Verify(string secret, byte[] salt, byte[] expectedHash, int iterations)
    {
        Validate(secret, iterations);
        var actual = Derive(secret, salt, iterations);
        return actual.Length == expectedHash.Length &&
               CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }

    private static byte[] Derive(string secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

    private static void Validate(string secret, int iterations)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("A device secret is required.", nameof(secret));
        }

        if (iterations < 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }
    }
}

