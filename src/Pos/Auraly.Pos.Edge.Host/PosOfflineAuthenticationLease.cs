using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Auraly.Contracts.Authentication;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Auraly.Pos.Edge.Host;

public sealed class PosOfflineLeaseTrustOptions
{
    public const string SectionName = "PosEdge:OfflineLeaseTrust";
    public Dictionary<string, string> TrustedPublicKeys { get; init; } =
        new(StringComparer.Ordinal);
}

public sealed record PosValidatedOfflineLease(
    OfflineAuthenticationLeasePayload Payload,
    SignedOfflineAuthenticationLease SignedLease);

public sealed class PosOfflineLeaseVerifier(
    IOptions<PosOfflineLeaseTrustOptions> options)
{
    private readonly IReadOnlyDictionary<string, string> _keys =
        options.Value.TrustedPublicKeys;

    public PosValidatedOfflineLease Verify(
        SignedOfflineAuthenticationLease lease,
        Guid expectedTenantId,
        Guid expectedDeviceId,
        DateTimeOffset now)
    {
        if (!string.Equals(
                lease.Algorithm,
                OfflineAuthenticationLeaseAlgorithms.RsaPssSha256,
                StringComparison.Ordinal))
            throw Invalid("Unsupported offline lease signature algorithm.");
        if (!_keys.TryGetValue(lease.KeyId, out var pem) ||
            string.IsNullOrWhiteSpace(pem))
            throw Invalid("The offline lease signing key is not trusted by this device.");

        var payloadBytes = Decode(lease.Payload);
        var signature = Decode(lease.Signature);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        if (!rsa.VerifyData(
                payloadBytes,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss))
            throw Invalid("The offline lease signature is invalid.");

        var payload = OfflineAuthenticationLeaseTokenCodec.Deserialize(payloadBytes);
        if (payload.Version != 1 || payload.TenantId != expectedTenantId ||
            payload.DeviceId != expectedDeviceId || payload.LeaseId == Guid.Empty ||
            payload.UserId == Guid.Empty || payload.Nonce == Guid.Empty)
            throw Invalid("The offline lease does not belong to this enrolled device.");
        if (payload.IssuedAt > payload.NotBefore || payload.NotBefore > now ||
            payload.ExpiresAt <= now)
            throw new PosLocalLoginException(
                "OfflineLeaseExpired",
                "La autorización para trabajar sin conexión venció. Conecta el equipo con Auraly.");
        return new PosValidatedOfflineLease(payload, lease);
    }

    private static byte[] Decode(string value)
    {
        try
        {
            return OfflineAuthenticationLeaseTokenCodec.Decode(value);
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException)
        {
            throw Invalid("The offline lease encoding is invalid.");
        }
    }

    private static PosLocalLoginException Invalid(string message) =>
        new("OfflineLeaseInvalid", message);
}

public sealed class PosOfflineLeaseStore(
    string connectionString,
    Guid tenantId,
    Guid deviceId,
    PosOfflineLeaseVerifier verifier,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(2);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PosOfflineAuthenticationLeases(
                LeaseId TEXT NOT NULL PRIMARY KEY,
                TenantId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                DeviceId TEXT NOT NULL,
                KeyId TEXT NOT NULL,
                Algorithm TEXT NOT NULL,
                SignedPayload TEXT NOT NULL,
                Signature TEXT NOT NULL,
                IssuedAt TEXT NOT NULL,
                NotBefore TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                LastObservedAt TEXT NOT NULL,
                Status TEXT NOT NULL,
                ReleaseAttempts INTEGER NOT NULL DEFAULT 0,
                LastReleaseError TEXT NULL,
                UpdatedAt TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_PosOfflineAuthenticationLeases_User_Active
                ON PosOfflineAuthenticationLeases(UserId)
                WHERE Status='Active';
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PosValidatedOfflineLease> SaveAsync(
        OfflineAuthenticationLeaseAcquireResponse response,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var validated = verifier.Verify(response.Lease, tenantId, deviceId, now);
        if (validated.Payload.UserId != response.User.UserId)
            throw new PosLocalLoginException(
                "OfflineLeaseInvalid",
                "La autorización offline no coincide con el usuario recibido.");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE PosOfflineAuthenticationLeases
            SET Status='Expired',UpdatedAt=$now
            WHERE Status='Active' AND ExpiresAt<=$now;
            INSERT INTO PosOfflineAuthenticationLeases(
                LeaseId,TenantId,UserId,DeviceId,KeyId,Algorithm,
                SignedPayload,Signature,IssuedAt,NotBefore,ExpiresAt,
                LastObservedAt,Status,UpdatedAt)
            VALUES(
                $lease,$tenant,$user,$device,$key,$algorithm,
                $payload,$signature,$issued,$notBefore,$expires,
                $now,'Active',$now)
            ON CONFLICT(LeaseId) DO UPDATE SET
                KeyId=excluded.KeyId,Algorithm=excluded.Algorithm,
                SignedPayload=excluded.SignedPayload,Signature=excluded.Signature,
                ExpiresAt=excluded.ExpiresAt,LastObservedAt=excluded.LastObservedAt,
                Status='Active',ReleaseAttempts=0,LastReleaseError=NULL,
                UpdatedAt=excluded.UpdatedAt;
            """;
        Add(command, validated, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return validated;
    }

    public async Task<PosValidatedOfflineLease> RequireForUserAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            SELECT l.KeyId,l.Algorithm,l.SignedPayload,l.Signature,l.LastObservedAt
            FROM PosOfflineAuthenticationLeases l
            INNER JOIN PosOfflineUsers u ON u.UserId=l.UserId
            WHERE u.NormalizedUsername=$username AND l.Status='Active';
            """;
        command.Parameters.AddWithValue("$username", Normalize(username));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new PosLocalLoginException(
                "OfflineLeaseRequired",
                "Este usuario no tiene una autorización offline vigente en este equipo.");
        var signed = new SignedOfflineAuthenticationLease(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        var lastObserved = DateTimeOffset.Parse(reader.GetString(4));
        await reader.DisposeAsync();
        if (now + ClockRollbackTolerance < lastObserved)
            throw new PosLocalLoginException(
                "ClockRollbackDetected",
                "El reloj del equipo retrocedió. Conecta el equipo con Auraly para validar el acceso.");
        var validated = verifier.Verify(signed, tenantId, deviceId, now);
        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = """
            UPDATE PosOfflineAuthenticationLeases
            SET LastObservedAt=$now,UpdatedAt=$now
            WHERE LeaseId=$lease AND Status='Active';
            """;
        update.Parameters.AddWithValue("$now", Format(now > lastObserved ? now : lastObserved));
        update.Parameters.AddWithValue("$lease", validated.Payload.LeaseId.ToString("D"));
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return validated;
    }

    public async Task QueueReleaseAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PosOfflineAuthenticationLeases
            SET Status='ReleasePending',UpdatedAt=$now
            WHERE UserId=$user AND Status='Active';
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Guid?> PendingReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT LeaseId
            FROM PosOfflineAuthenticationLeases
            WHERE Status='ReleasePending'
            ORDER BY UpdatedAt
            LIMIT 1;
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text ? Guid.Parse(text) : null;
    }

    public async Task MarkReleasedAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PosOfflineAuthenticationLeases
            SET Status='Released',LastReleaseError=NULL,UpdatedAt=$now
            WHERE LeaseId=$lease AND Status='ReleasePending';
            """;
        command.Parameters.AddWithValue("$lease", leaseId.ToString("D"));
        command.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Add(
        SqliteCommand command,
        PosValidatedOfflineLease validated,
        DateTimeOffset now)
    {
        var payload = validated.Payload;
        var signed = validated.SignedLease;
        command.Parameters.AddWithValue("$lease", payload.LeaseId.ToString("D"));
        command.Parameters.AddWithValue("$tenant", payload.TenantId.ToString("D"));
        command.Parameters.AddWithValue("$user", payload.UserId.ToString("D"));
        command.Parameters.AddWithValue("$device", payload.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$key", signed.KeyId);
        command.Parameters.AddWithValue("$algorithm", signed.Algorithm);
        command.Parameters.AddWithValue("$payload", signed.Payload);
        command.Parameters.AddWithValue("$signature", signed.Signature);
        command.Parameters.AddWithValue("$issued", Format(payload.IssuedAt));
        command.Parameters.AddWithValue("$notBefore", Format(payload.NotBefore));
        command.Parameters.AddWithValue("$expires", Format(payload.ExpiresAt));
        command.Parameters.AddWithValue("$now", Format(now));
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string Format(DateTimeOffset value) => value.ToString("O");
}

public sealed class PosOfflineLeaseClient(
    HttpClient http,
    PosDeviceCredentials credentials)
{
    public async Task<OfflineAuthenticationLeaseAcquireResponse> AcquireAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/pos/v1/authentication/offline-leases");
        AddDeviceHeaders(message);
        message.Content = JsonContent.Create(
            new OfflineAuthenticationLeaseAcquireRequest(
                request.Username, request.Password));
        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var code = response.StatusCode == HttpStatusCode.Conflict
                ? "OfflineLeaseConflict"
                : "InvalidCredentials";
            throw new PosLocalLoginException(
                code,
                response.StatusCode == HttpStatusCode.Conflict
                    ? "El usuario ya tiene una sesión activa en otro equipo."
                    : "Usuario o contraseña incorrectos.");
        }
        return await response.Content.ReadFromJsonAsync<OfflineAuthenticationLeaseAcquireResponse>(
            cancellationToken)
            ?? throw new InvalidDataException("Auraly Server returned an empty offline lease.");
    }

    public async Task ReleaseAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/authentication/offline-leases/{leaseId:D}/release");
        AddDeviceHeaders(message);
        using var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private void AddDeviceHeaders(HttpRequestMessage message)
    {
        message.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        message.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
    }
}
