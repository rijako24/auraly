using System.Data;
using System.Security.Cryptography;
using Auraly.Application.Authentication;
using Auraly.Contracts.Authentication;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Auraly.Infrastructure.Persistence;

public sealed class OfflineAuthenticationLeaseSigningOptions
{
    public const string SectionName = "Authentication:OfflineLeaseSigning";
    public string KeyId { get; init; } = string.Empty;
    public string PrivateKeyPem { get; init; } = string.Empty;
}

public sealed class RsaOfflineAuthenticationLeaseSigner :
    IOfflineAuthenticationLeaseSigner,
    IOfflineAuthenticationLeaseTrustProvider,
    IDisposable
{
    private readonly OfflineAuthenticationLeaseSigningOptions _options;
    private RSA? _key;
    private string? _keyId;
    private readonly object _sync = new();
    public IReadOnlyDictionary<string, string> TrustedPublicKeys
    {
        get
        {
            lock (_sync)
            {
                EnsureInitialized();
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [_keyId!] = _key!.ExportSubjectPublicKeyInfoPem()
                };
            }
        }
    }

    public RsaOfflineAuthenticationLeaseSigner(
        IOptions<OfflineAuthenticationLeaseSigningOptions> options)
    {
        _options = options.Value;
    }

    public SignedOfflineAuthenticationLease Sign(
        OfflineAuthenticationLeasePayload payload)
    {
        var bytes = OfflineAuthenticationLeaseTokenCodec.Serialize(payload);
        byte[] signature;
        lock (_sync)
        {
            EnsureInitialized();
            signature = _key!.SignData(
                bytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }
        return new SignedOfflineAuthenticationLease(
            _keyId!,
            OfflineAuthenticationLeaseAlgorithms.RsaPssSha256,
            OfflineAuthenticationLeaseTokenCodec.Encode(bytes),
            OfflineAuthenticationLeaseTokenCodec.Encode(signature));
    }

    private void EnsureInitialized()
    {
        if (_key is not null) return;
        if (string.IsNullOrWhiteSpace(_options.KeyId) ||
            string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
            throw new OfflineAuthenticationLeaseConfigurationException(
                "La firma segura para el acceso sin conexión no está configurada en el servidor.");

        var key = RSA.Create();
        try
        {
            key.ImportFromPem(_options.PrivateKeyPem);
            if (key.KeySize < 2048)
                throw new OfflineAuthenticationLeaseConfigurationException(
                    "La clave de firma para el acceso sin conexión debe ser RSA de al menos 2048 bits.");
            _keyId = _options.KeyId.Trim();
            _key = key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_sync) _key?.Dispose();
    }
}

public sealed class SqlOfflineAuthenticationLeaseStore(
    SqlServerConnectionFactory connections) : IOfflineAuthenticationLeaseStore
{
    public async Task<SignedOfflineAuthenticationLease> AcquireAsync(
        OfflineAuthenticationLeaseCandidate candidate,
        CancellationToken cancellationToken)
    {
        var payload = candidate.Payload;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        await LockUserAsync(connection, transaction, payload, cancellationToken);
        await EnsureDeviceAsync(connection, transaction, payload, cancellationToken);
        await ExpireStaleAsync(connection, transaction, payload, cancellationToken);

        var existing = await ReadActiveAsync(
            connection, transaction, payload.TenantId, payload.UserId,
            payload.DeviceId, cancellationToken);
        if (existing is not null)
        {
            await UpdateOfflineVerifierAsync(
                connection, transaction, candidate, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }
        if (await HasConflictingLeaseAsync(
                connection, transaction, payload, cancellationToken))
            throw new OfflineAuthenticationLeaseConflictException(
                "The user or enrolled device already owns another active offline lease.");
        if (await HasActiveOnlineSessionAsync(
                connection, transaction, payload, cancellationToken))
            throw new OfflineAuthenticationLeaseConflictException(
                "The user already has an active online session.");

        await UpdateOfflineVerifierAsync(
            connection, transaction, candidate, cancellationToken);
        await InsertAsync(connection, transaction, candidate, cancellationToken);
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new OfflineAuthenticationLeaseConflictException(
                "The offline lease was acquired concurrently by another session.");
        }
        return candidate.SignedLease;
    }

    public async Task ReleaseAsync(
        Guid tenantId,
        Guid deviceId,
        Guid leaseId,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE dbo.OfflineAuthenticationLeases WITH (UPDLOCK,HOLDLOCK)
            SET Status=N'Released',EndedAt=@Now,EndReason=N'UserLogout',UpdatedAt=@Now
            WHERE LeaseId=@LeaseId AND TenantId=@TenantId AND DeviceId=@DeviceId
              AND Status=N'Active';
            SELECT Status
            FROM dbo.OfflineAuthenticationLeases WITH (UPDLOCK,HOLDLOCK)
            WHERE LeaseId=@LeaseId AND TenantId=@TenantId AND DeviceId=@DeviceId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@LeaseId", leaseId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        command.Parameters.AddWithValue("@Now", releasedAt);
        var status = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (status is null)
            throw new AuthenticationDeniedException(
                "The offline authentication lease does not belong to this device.");
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task LockUserAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OfflineAuthenticationLeasePayload payload,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT UserId
            FROM dbo.AppUsers WITH (UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId AND UserId=@UserId AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        command.Parameters.AddWithValue("@UserId", payload.UserId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not Guid)
            throw new AuthenticationDeniedException("The user is inactive or missing.");
    }

    private static async Task EnsureDeviceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OfflineAuthenticationLeasePayload payload,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT d.DeviceId
            FROM dbo.EnrolledDevices d WITH (UPDLOCK,HOLDLOCK)
            WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId
              AND d.IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DeviceId", payload.DeviceId);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not Guid)
            throw new AuthenticationDeniedException(
                "The enrolled device is inactive or belongs to another tenant.");
    }

    private static async Task ExpireStaleAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OfflineAuthenticationLeasePayload payload,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.OfflineAuthenticationLeases WITH (UPDLOCK,HOLDLOCK)
            SET Status=N'Expired',EndedAt=@Now,EndReason=N'Expired',UpdatedAt=@Now
            WHERE Status=N'Active' AND ExpiresAt<=@Now
              AND (TenantId=@TenantId OR DeviceId=@DeviceId);
            UPDATE dbo.AuthenticationSessions WITH (UPDLOCK,HOLDLOCK)
            SET Status=N'Expired',RevokedAt=@Now,
                RevocationReason=N'Expired',UpdatedAt=@Now
            WHERE TenantId=@TenantId AND UserId=@UserId
              AND Status=N'Active' AND ExpiresAt<=@Now;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Now", payload.IssuedAt);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        command.Parameters.AddWithValue("@UserId", payload.UserId);
        command.Parameters.AddWithValue("@DeviceId", payload.DeviceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SignedOfflineAuthenticationLease?> ReadActiveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT KeyId,Algorithm,SignedPayload,Signature
            FROM dbo.OfflineAuthenticationLeases WITH (UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId AND UserId=@UserId AND DeviceId=@DeviceId
              AND Status=N'Active';
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SignedOfflineAuthenticationLease(
                reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3))
            : null;
    }

    private static async Task<bool> HasConflictingLeaseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OfflineAuthenticationLeasePayload payload,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT LeaseId
            FROM dbo.OfflineAuthenticationLeases WITH (UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId AND Status=N'Active'
              AND (UserId=@UserId OR DeviceId=@DeviceId);
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        command.Parameters.AddWithValue("@UserId", payload.UserId);
        command.Parameters.AddWithValue("@DeviceId", payload.DeviceId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid;
    }

    private static async Task<bool> HasActiveOnlineSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OfflineAuthenticationLeasePayload payload,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT AuthenticationSessionId
            FROM dbo.AuthenticationSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId AND UserId=@UserId AND Status=N'Active';
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        command.Parameters.AddWithValue("@UserId", payload.UserId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid;
    }

    private static async Task UpdateOfflineVerifierAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OfflineAuthenticationLeaseCandidate candidate,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.AppUsers
            SET PosOfflinePasswordSalt=@Salt,
                PosOfflinePasswordHash=@Hash,
                PosOfflinePasswordIterations=@Iterations,
                PosOfflinePasswordChangedAt=@ChangedAt,
                AccessFailedCount=0,LockoutEnd=NULL,UpdatedAt=@Now
            WHERE TenantId=@TenantId AND UserId=@UserId AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", candidate.Payload.TenantId);
        command.Parameters.AddWithValue("@UserId", candidate.Payload.UserId);
        command.Parameters.Add("@Salt", SqlDbType.VarBinary, 16).Value =
            candidate.PasswordVerifier.Salt;
        command.Parameters.Add("@Hash", SqlDbType.VarBinary, 32).Value =
            candidate.PasswordVerifier.Hash;
        command.Parameters.AddWithValue("@Iterations", candidate.PasswordVerifier.Iterations);
        command.Parameters.AddWithValue("@ChangedAt", candidate.PasswordVerifier.ChangedAt);
        command.Parameters.AddWithValue("@Now", candidate.Payload.IssuedAt);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new AuthenticationDeniedException("The user is inactive or missing.");
    }

    private static async Task InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OfflineAuthenticationLeaseCandidate candidate,
        CancellationToken cancellationToken)
    {
        var payload = candidate.Payload;
        var signed = candidate.SignedLease;
        await using var command = new SqlCommand("""
            INSERT dbo.OfflineAuthenticationLeases
              (LeaseId,TenantId,UserId,DeviceId,KeyId,Algorithm,
               SignedPayload,Signature,Nonce,IssuedAt,NotBefore,ExpiresAt,Status)
            VALUES
              (@LeaseId,@TenantId,@UserId,@DeviceId,@KeyId,@Algorithm,
               @Payload,@Signature,@Nonce,@IssuedAt,@NotBefore,@ExpiresAt,N'Active');
            """, connection, transaction);
        command.Parameters.AddWithValue("@LeaseId", payload.LeaseId);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        command.Parameters.AddWithValue("@UserId", payload.UserId);
        command.Parameters.AddWithValue("@DeviceId", payload.DeviceId);
        command.Parameters.AddWithValue("@KeyId", signed.KeyId);
        command.Parameters.AddWithValue("@Algorithm", signed.Algorithm);
        command.Parameters.AddWithValue("@Payload", signed.Payload);
        command.Parameters.AddWithValue("@Signature", signed.Signature);
        command.Parameters.AddWithValue("@Nonce", payload.Nonce);
        command.Parameters.AddWithValue("@IssuedAt", payload.IssuedAt);
        command.Parameters.AddWithValue("@NotBefore", payload.NotBefore);
        command.Parameters.AddWithValue("@ExpiresAt", payload.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
