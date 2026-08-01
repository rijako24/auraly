using System.Text.Json;

namespace Auraly.Contracts.Authentication;

public static class OfflineAuthenticationLeaseAlgorithms
{
    public const string RsaPssSha256 = "PS256";
}

public sealed record OfflineAuthenticationLeasePayload(
    int Version,
    Guid LeaseId,
    Guid TenantId,
    Guid UserId,
    Guid DeviceId,
    DateTimeOffset IssuedAt,
    DateTimeOffset NotBefore,
    DateTimeOffset ExpiresAt,
    Guid Nonce);

public sealed record SignedOfflineAuthenticationLease(
    string KeyId,
    string Algorithm,
    string Payload,
    string Signature);

public sealed record OfflineAuthenticationLeaseAcquireRequest(
    string Username,
    string Password);

public sealed record OfflineAuthenticationLeaseUser(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    byte[] PasswordSalt,
    byte[] PasswordHash,
    int PasswordIterations,
    DateTimeOffset PasswordChangedAt);

public sealed record OfflineAuthenticationLeaseAcquireResponse(
    SignedOfflineAuthenticationLease Lease,
    OfflineAuthenticationLeaseUser User);

public static class OfflineAuthenticationLeaseTokenCodec
{
    public static byte[] Serialize(OfflineAuthenticationLeasePayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);

    public static OfflineAuthenticationLeasePayload Deserialize(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<OfflineAuthenticationLeasePayload>(payload, SerializerOptions)
        ?? throw new InvalidDataException("The offline authentication lease payload is empty.");

    public static string Encode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException("The offline authentication lease is not valid Base64Url.")
        };
        return Convert.FromBase64String(normalized);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
