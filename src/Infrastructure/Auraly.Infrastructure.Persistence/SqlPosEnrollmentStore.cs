using System.Security.Cryptography;
using Auraly.Application.Authentication;
using Auraly.Application.Organization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosEnrollmentStore(
    SqlServerConnectionFactory connections,
    IFiscalTechnicalKeyProvider technicalKeys,
    TimeProvider timeProvider,
    IAuralyIdGenerator idGenerator,
    IOfflineAuthenticationLeaseTrustProvider offlineLeaseTrust) : IPosEnrollmentStore
{
    public async Task<OnlineRegisterContext?> ResolveRegisterAsync(
        Guid tenantId,
        CreatePosEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.BusinessId,b.Name,
                   r.RegisterId,r.Code,r.Name,w.WarehouseId,w.Code,w.Name,
                   w.AllowNegativeStockSales
            FROM dbo.Businesses b
            JOIN dbo.CashRegisters r ON r.BusinessId=b.BusinessId AND r.IsActive=1
            JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
                AND w.BusinessId=b.BusinessId AND w.IsActive=1
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
              AND r.RegisterId=@RegisterId
              AND b.IsActive=1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@RegisterId", request.RegisterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadRegister(reader);
    }

    public async Task CreateAuthorizationAsync(
        PosEnrollmentAuthorizationCommand command,
        OnlineRegisterContext register,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            SELECT RegisterId FROM dbo.CashRegisters WITH (UPDLOCK,HOLDLOCK)
            WHERE RegisterId=@RegisterId AND IsActive=1;
            IF @@ROWCOUNT=0 THROW 51000,'The register is unavailable.',1;
            IF EXISTS (
                SELECT 1 FROM dbo.PosDevices
                WHERE RegisterId=@RegisterId AND IsActive=1)
                THROW 51001,'The register already has an active Edge enrollment.',1;
            UPDATE dbo.PosEnrollmentSessions
            SET ExpiresAt=@Now
            WHERE RegisterId=@RegisterId AND RedeemedAt IS NULL AND ExpiresAt>@Now;
            INSERT dbo.PosEnrollmentSessions
              (EnrollmentSessionId,TenantId,BusinessId,WarehouseId,
               RegisterId,RequestedByUserId,RequestedByDisplayName,DeviceName,
               RedemptionCodeHash,ExpiresAt,CreatedAt)
            VALUES
              (@SessionId,@TenantId,@BusinessId,@WarehouseId,
               @RegisterId,@UserId,@DisplayName,@DeviceName,
               @CodeHash,@ExpiresAt,@Now);
            COMMIT;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var sqlCommand = new SqlCommand(sql, connection);
        Add(sqlCommand, "@SessionId", command.EnrollmentSessionId);
        Add(sqlCommand, "@TenantId", command.User.TenantId);
        Add(sqlCommand, "@BusinessId", register.BusinessId);
        Add(sqlCommand, "@WarehouseId", register.WarehouseId);
        Add(sqlCommand, "@RegisterId", register.RegisterId);
        Add(sqlCommand, "@UserId", command.User.UserId);
        Add(sqlCommand, "@DisplayName", command.User.DisplayName);
        Add(sqlCommand, "@DeviceName", command.Request.DeviceName);
        Add(sqlCommand, "@CodeHash", command.RedemptionCodeHash);
        Add(sqlCommand, "@ExpiresAt", command.ExpiresAt);
        Add(sqlCommand, "@Now", timeProvider.GetUtcNow());
        try { await sqlCommand.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number == 51001)
        {
            throw new PosEnrollmentConflictException(
                "La caja ya está enrolada en otro equipo. Debe revocarse explícitamente antes de reasignarla.");
        }
    }

    public async Task<PosEnrollmentPackage> RedeemAsync(
        RedeemPosEnrollmentRequest request,
        byte[] redemptionCodeHash,
        IReadOnlyCollection<string> devicePermissions,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var data = await ReadProvisioningAsync(
            connection, transaction, request.EnrollmentSessionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (data is null ||
            data.RedeemedAt is not null ||
            data.ExpiresAt <= now ||
            !FixedEquals(data.CodeHash, redemptionCodeHash))
        {
            throw new PosEnrollmentValidationException(
                "El código de enrolamiento es inválido, expiró o ya fue utilizado.");
        }

        if (await HasActiveDeviceAsync(
                connection, transaction, data.RegisterId, cancellationToken))
            throw new PosEnrollmentConflictException(
                "La caja ya está enrolada en otro equipo. Debe revocarse explícitamente antes de reasignarla.");

        var material = await technicalKeys.ResolveAsync(
            new FiscalKeyReference(
                data.TenantId, data.BusinessId, data.AuthorizationNumber,
                data.TechnicalKeyVersion, (FiscalEnvironment)data.Environment),
            cancellationToken)
            ?? throw new PosEnrollmentValidationException(
                "La clave técnica de la resolución no está disponible en el almacenamiento seguro del servidor.");
        if (!string.Equals(material.SupplierTaxId, data.SupplierTaxId, StringComparison.Ordinal) ||
            !string.Equals(material.QrValidationUrl, data.QrValidationUrl, StringComparison.Ordinal))
            throw new PosEnrollmentValidationException(
                "La configuración fiscal segura no coincide con la resolución asignada.");

        var deviceId = idGenerator.NewId();
        var deviceSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var credential = PosDeviceCredentialHasher.Create(deviceSecret);
        await InsertDeviceAsync(
            connection, transaction, data, deviceId, deviceSecret, credential,
            devicePermissions, request.InstallationId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PosEnrollmentPackage(
            deviceId, deviceSecret, data.TenantId, data.BusinessId,
            data.WarehouseId, data.RegisterId, data.RegisterCode, data.RegisterName,
            data.AllowNegativeStock, data.UserId, data.UserDisplayName,
            devicePermissions.Order(StringComparer.Ordinal).ToArray(),
            new PosEnrollmentDocumentSeries(
                data.DocumentSeriesId, data.DocumentType, data.DocumentPrefix,
                data.SeriesCode, data.Padding, data.DocumentRangeStart,
                data.DocumentRangeEnd),
            new PosEnrollmentFiscalSeries(
                data.FiscalSeriesId, data.FiscalAuthorizationId, data.FiscalPrefix,
                data.AuthorizationNumber, data.FiscalRangeStart, data.FiscalRangeEnd,
                data.ValidUntil, data.Environment, data.SupplierTaxId,
                new string(material.TechnicalKey.Reveal()), data.TechnicalKeyVersion,
                data.QrValidationUrl),
            offlineLeaseTrust.TrustedPublicKeys,
            now);
    }

    private static async Task<Provisioning?> ReadProvisioningAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(2)
              e.TenantId,e.BusinessId,e.WarehouseId,e.RegisterId,
              r.Code,r.Name,w.AllowNegativeStockSales,e.RequestedByUserId,
              e.RequestedByDisplayName,e.RedemptionCodeHash,e.ExpiresAt,e.RedeemedAt,
              ds.DocumentSeriesId,ds.DocumentType,ds.Prefix,ds.SeriesCode,
              ds.Padding,ds.RangeStart,ds.RangeEnd,
              fs.SeriesId,fs.FiscalAuthorizationId,fs.Prefix,fs.RangeStart,fs.RangeEnd,
              fa.AuthorizationNumber,fa.ValidUntil,fa.Environment,fa.SupplierTaxId,
              fa.TechnicalKeyVersion,fa.QrValidationUrl
            FROM dbo.PosEnrollmentSessions e WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.CashRegisters r ON r.RegisterId=e.RegisterId AND r.IsActive=1
            JOIN dbo.Warehouses w ON w.WarehouseId=e.WarehouseId AND w.IsActive=1
            JOIN dbo.DocumentSeries ds ON ds.RegisterId=e.RegisterId
                AND ds.BusinessId=e.BusinessId
                AND ds.DocumentType=N'SalesInvoice' AND ds.IsOfflineCapable=1 AND ds.IsActive=1
            JOIN dbo.FiscalSeries fs ON fs.RegisterId=e.RegisterId
                AND fs.BusinessId=e.BusinessId AND fs.DocumentType=N'SalesInvoice' AND fs.IsActive=1
            JOIN dbo.FiscalAuthorizations fa ON fa.FiscalAuthorizationId=fs.FiscalAuthorizationId
                AND fa.BusinessId=e.BusinessId AND fa.IsActive=1
            WHERE e.EnrollmentSessionId=@SessionId
            ORDER BY ds.DocumentSeriesId,fs.SeriesId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var value = new Provisioning(
            sessionId, reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetBoolean(6),
            reader.GetGuid(7), reader.GetString(8), (byte[])reader[9],
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetGuid(12), reader.GetString(13), reader.GetString(14), reader.GetString(15),
            reader.GetByte(16), reader.GetInt64(17), reader.GetInt64(18),
            reader.GetGuid(19), reader.GetGuid(20), reader.GetString(21),
            reader.GetInt64(22), reader.GetInt64(23), reader.GetString(24),
            DateOnly.FromDateTime(reader.GetDateTime(25)), reader.GetByte(26),
            reader.GetString(27), reader.GetString(28), reader.GetString(29));
        if (await reader.ReadAsync(cancellationToken))
            throw new PosEnrollmentValidationException(
                "La caja debe tener exactamente una serie operativa y una serie fiscal offline activas.");
        return value;
    }

    private static async Task<bool> HasActiveDeviceAsync(
        SqlConnection connection, SqlTransaction transaction, Guid registerId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.PosDevices WITH (UPDLOCK,HOLDLOCK) WHERE RegisterId=@RegisterId AND IsActive=1;",
            connection, transaction);
        command.Parameters.AddWithValue("@RegisterId", registerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task InsertDeviceAsync(
        SqlConnection connection, SqlTransaction transaction, Provisioning data,
        Guid deviceId, string deviceSecret, PosDeviceCredential credential,
        IReadOnlyCollection<string> permissions, string installationId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.PosDevices
              (DeviceId,BusinessId,WarehouseId,RegisterId,Name,
               CredentialSalt,CredentialHash,CredentialIterations,IsActive,CreatedAt)
            VALUES
              (@DeviceId,@BusinessId,@WarehouseId,@RegisterId,@Name,
               @Salt,@Hash,@Iterations,1,@Now);
            UPDATE dbo.PosEnrollmentSessions
            SET RedeemedAt=@Now,DeviceId=@DeviceId
            WHERE EnrollmentSessionId=@SessionId AND RedeemedAt IS NULL;
            """;
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            Add(command, "@DeviceId", deviceId);
            Add(command, "@BusinessId", data.BusinessId);
            Add(command, "@WarehouseId", data.WarehouseId);
            Add(command, "@RegisterId", data.RegisterId);
            Add(command, "@Name", installationId);
            Add(command, "@Salt", credential.Salt);
            Add(command, "@Hash", credential.Hash);
            Add(command, "@Iterations", credential.Iterations);
            Add(command, "@Now", now);
            Add(command, "@SessionId", data.SessionId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var permission in permissions)
        {
            await using var permissionCommand = new SqlCommand("""
                INSERT dbo.PosDevicePermissions
                  (DeviceId,PermissionCode,IsGranted,GrantedAt)
                VALUES (@DeviceId,@Permission,1,@Now);
                """, connection, transaction);
            Add(permissionCommand, "@DeviceId", deviceId);
            Add(permissionCommand, "@Permission", permission);
            Add(permissionCommand, "@Now", now);
            await permissionCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static bool FixedEquals(byte[] left, byte[] right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

    private static OnlineRegisterContext ReadRegister(SqlDataReader reader) =>
        new(
            reader.GetGuid(0), reader.GetString(1),
            reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
            reader.GetGuid(5), reader.GetString(6), reader.GetString(7), reader.GetBoolean(8));

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    private sealed record Provisioning(
        Guid SessionId, Guid TenantId, Guid BusinessId, Guid WarehouseId, Guid RegisterId,
        string RegisterCode, string RegisterName, bool AllowNegativeStock,
        Guid UserId, string UserDisplayName, byte[] CodeHash, DateTimeOffset ExpiresAt,
        DateTimeOffset? RedeemedAt, Guid DocumentSeriesId, string DocumentType,
        string DocumentPrefix, string SeriesCode, byte Padding, long DocumentRangeStart,
        long DocumentRangeEnd, Guid FiscalSeriesId, Guid FiscalAuthorizationId,
        string FiscalPrefix, long FiscalRangeStart, long FiscalRangeEnd,
        string AuthorizationNumber, DateOnly ValidUntil, byte Environment,
        string SupplierTaxId, string TechnicalKeyVersion, string QrValidationUrl);
}
