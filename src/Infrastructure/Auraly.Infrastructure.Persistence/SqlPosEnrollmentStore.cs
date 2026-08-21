using System.Globalization;
using System.Security.Cryptography;
using Auraly.Application.Authentication;
using Auraly.Application.Organization;
using Auraly.BuildingBlocks.Domain.Documents;
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
    public async Task<SalesWorkspaceContext?> ResolveWorkspaceAsync(
        Guid tenantId,
        CreatePosEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.BusinessId,b.Name,w.WarehouseId,w.Code,w.Name,
                   w.AllowNegativeStockSales
            FROM dbo.Businesses b
            JOIN dbo.Warehouses w
              ON w.BusinessId=b.BusinessId AND w.IsActive=1 AND w.UseForSales=1
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
              AND w.WarehouseId=@WarehouseId AND b.IsActive=1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", request.BusinessId);
        Add(command, "@WarehouseId", request.WarehouseId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadWorkspace(reader);
    }

    public async Task CreateAuthorizationAsync(
        PosEnrollmentAuthorizationCommand command,
        SalesWorkspaceContext workspace,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            SELECT WarehouseId FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND IsActive=1 AND UseForSales=1;
            IF @@ROWCOUNT=0 THROW 51000,'The sales workspace is unavailable.',1;
            UPDATE dbo.PosEnrollmentSessions
            SET ExpiresAt=@Now
            WHERE TenantId=@TenantId AND RequestedByUserId=@UserId
              AND DeviceName=@DeviceName AND RedeemedAt IS NULL AND ExpiresAt>@Now;
            INSERT dbo.PosEnrollmentSessions
              (EnrollmentSessionId,TenantId,BusinessId,WarehouseId,
               RequestedByUserId,RequestedByDisplayName,DeviceName,
               RedemptionCodeHash,ExpiresAt,CreatedAt)
            VALUES
              (@SessionId,@TenantId,@BusinessId,@WarehouseId,
               @UserId,@DisplayName,@DeviceName,@CodeHash,@ExpiresAt,@Now);
            COMMIT;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var sqlCommand = new SqlCommand(sql, connection);
        Add(sqlCommand, "@SessionId", command.EnrollmentSessionId);
        Add(sqlCommand, "@TenantId", command.User.TenantId);
        Add(sqlCommand, "@BusinessId", workspace.BusinessId);
        Add(sqlCommand, "@WarehouseId", workspace.WarehouseId);
        Add(sqlCommand, "@UserId", command.User.UserId);
        Add(sqlCommand, "@DisplayName", command.User.DisplayName);
        Add(sqlCommand, "@DeviceName", command.Request.DeviceName);
        Add(sqlCommand, "@CodeHash", command.RedemptionCodeHash);
        Add(sqlCommand, "@ExpiresAt", command.ExpiresAt);
        Add(sqlCommand, "@Now", timeProvider.GetUtcNow());
        await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
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
        var existing = request.ExistingDeviceId is { } existingDeviceId
            ? await ReadExistingDeviceAsync(
                connection, transaction, existingDeviceId, cancellationToken)
            : null;
        if (request.ExistingDeviceId.HasValue && existing is null)
            throw new PosEnrollmentValidationException(
                "El equipo indicado no está enrolado o su serie operativa ya no está activa.");
        var data = await ReadProvisioningAsync(
            connection, transaction, request.EnrollmentSessionId,
            existing?.DeviceId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (data is null || data.RedeemedAt is not null || data.ExpiresAt <= now ||
            !FixedEquals(data.CodeHash, redemptionCodeHash))
            throw new PosEnrollmentValidationException(
                "El código de enrolamiento es inválido, expiró o ya fue utilizado.");
        if (existing is null)
            await EnsureDeviceCapacityAsync(connection, transaction, data.TenantId, cancellationToken);

        if (existing is not null &&
            (existing.TenantId != data.TenantId || existing.BusinessId != data.BusinessId))
            throw new PosEnrollmentConflictException(
                "Este equipo ya está asociado a otra sede. Conserva su identidad actual para no invalidar facturas offline pendientes.");

        var material = data.FiscalSeriesId is null
            ? null
            : await technicalKeys.ResolveAsync(
                new FiscalKeyReference(
                    data.TenantId, data.BusinessId, data.AuthorizationNumber!,
                    data.TechnicalKeyVersion!, (FiscalEnvironment)data.Environment!.Value),
                cancellationToken);
        if (data.FiscalSeriesId is not null && material is null)
            throw new PosEnrollmentValidationException(
                "La clave técnica de la resolución no está disponible en el almacenamiento seguro del servidor.");
        if (material is not null &&
            (!string.Equals(material.SupplierTaxId, data.SupplierTaxId, StringComparison.Ordinal) ||
             !string.Equals(material.QrValidationUrl, data.QrValidationUrl, StringComparison.Ordinal)))
            throw new PosEnrollmentValidationException(
                "La configuración fiscal segura no coincide con la resolución asignada.");

        var deviceId = existing?.DeviceId ?? idGenerator.NewId();
        var documentSeriesId = existing?.DocumentSeriesId ?? idGenerator.NewId();
        var deviceSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var credential = PosDeviceCredentialHasher.Create(deviceSecret);
        var documentSeries = existing is null
            ? new PosEnrollmentDocumentSeries(
                documentSeriesId, "SalesInvoice", "VTA",
                await AllocateSeriesCodeAsync(
                    connection, transaction, data.BusinessId, cancellationToken),
                8, 1, 99_999_999)
            : new PosEnrollmentDocumentSeries(
                existing.DocumentSeriesId, existing.DocumentType, existing.Prefix,
                existing.SeriesCode, existing.Padding, existing.RangeStart,
                existing.RangeEnd);

        if (existing is null)
        {
            await InsertDeviceAndSeriesAsync(
                connection, transaction, data, deviceId, documentSeriesId,
                documentSeries.SeriesCode, credential, devicePermissions,
                request.InstallationId, now, cancellationToken);
        }
        else
        {
            await UpdateExistingDeviceAsync(
                connection, transaction, data, existing, credential,
                devicePermissions, request.InstallationId, now, cancellationToken);
        }
        if (data.FiscalSeriesId is not null)
            await AssignFiscalSeriesAsync(
                connection, transaction, data, deviceId, cancellationToken);
        var receiptDocumentSeries = await EnsureReceiptDocumentSeriesAsync(
            connection, transaction, data.BusinessId, deviceId,
            documentSeries.SeriesCode, idGenerator.NewId(), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PosEnrollmentPackage(
            deviceId, deviceSecret, data.TenantId, data.BusinessId,
            data.WarehouseId, data.BusinessName, data.WarehouseCode,
            data.WarehouseName, data.AllowNegativeStock,
            data.UserId, data.UserDisplayName,
            devicePermissions.Order(StringComparer.Ordinal).ToArray(),
            documentSeries,
            material is null ? null : new PosEnrollmentFiscalSeries(
                data.FiscalSeriesId!.Value, data.FiscalAuthorizationId!.Value,
                data.FiscalPrefix!, data.AuthorizationNumber!,
                data.FiscalRangeStart!.Value, data.FiscalRangeEnd!.Value,
                data.ValidUntil!.Value, data.Environment!.Value, data.SupplierTaxId!,
                new string(material.TechnicalKey.Reveal()), data.TechnicalKeyVersion!,
                data.QrValidationUrl!, data.ValidFrom),
            receiptDocumentSeries,
            offlineLeaseTrust.TrustedPublicKeys,
            now);
    }

    private static async Task EnsureDeviceCapacityAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT t.MaximumEnrolledDevices,
                   (SELECT COUNT_BIG(1)
                    FROM dbo.EnrolledDevices devices WITH (UPDLOCK,HOLDLOCK)
                    WHERE devices.TenantId=t.TenantId AND devices.IsActive=1)
            FROM dbo.Tenants t WITH (UPDLOCK,HOLDLOCK)
            WHERE t.TenantId=@TenantId AND t.IsActive=1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@TenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new PosEnrollmentConflictException(
                "La organización no existe o está inactiva.");
        }

        var maximumDevices = reader.GetInt32(0);
        var activeDevices = reader.GetInt64(1);
        if (activeDevices >= maximumDevices)
        {
            throw new PosEnrollmentConflictException(
                $"La organización alcanzó el máximo de {maximumDevices} cajas enroladas permitido. Desactiva una caja o solicita a Auraly una ampliación de capacidad.");
        }
    }    private static async Task<ExistingDevice?> ReadExistingDeviceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.DeviceId,d.TenantId,ds.BusinessId,ds.DocumentSeriesId,
                   ds.DocumentType,ds.Prefix,ds.SeriesCode,ds.Padding,
                   ds.RangeStart,ds.RangeEnd
            FROM dbo.EnrolledDevices d WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.DocumentSeries ds WITH (UPDLOCK,HOLDLOCK)
              ON ds.DeviceId=d.DeviceId AND ds.DocumentType=N'SalesInvoice'
             AND ds.IsActive=1
            WHERE d.DeviceId=@DeviceId AND d.IsActive=1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@DeviceId", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ExistingDevice(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetString(4), reader.GetString(5),
            reader.GetString(6), reader.GetByte(7), reader.GetInt64(8),
            reader.GetInt64(9));
    }

    private static async Task UpdateExistingDeviceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Provisioning data,
        ExistingDevice existing,
        PosDeviceCredential credential,
        IReadOnlyCollection<string> permissions,
        string installationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.EnrolledDevices
            SET Name=@Name,CredentialSalt=@Salt,CredentialHash=@Hash,
                CredentialIterations=@Iterations,IsActive=1,LastSeenAt=@Now
            WHERE DeviceId=@DeviceId AND TenantId=@TenantId;
            IF @@ROWCOUNT<>1 THROW 51003,'The existing POS device is not valid for this tenant.',1;
            DELETE FROM dbo.PosDevicePermissions WHERE DeviceId=@DeviceId;
            UPDATE dbo.PosEnrollmentSessions
            SET RedeemedAt=@Now,DeviceId=@DeviceId
            WHERE EnrollmentSessionId=@SessionId AND RedeemedAt IS NULL;
            IF @@ROWCOUNT<>1 THROW 51002,'The enrollment session was already redeemed.',1;
            """;
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            Add(command, "@DeviceId", existing.DeviceId);
            Add(command, "@TenantId", data.TenantId);
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
            Add(permissionCommand, "@DeviceId", existing.DeviceId);
            Add(permissionCommand, "@Permission", permission);
            Add(permissionCommand, "@Now", now);
            await permissionCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }
    private static async Task<Provisioning?> ReadProvisioningAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid sessionId,
        Guid? existingDeviceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(1)
              e.TenantId,e.BusinessId,e.WarehouseId,b.Name,w.Code,w.Name,
              w.AllowNegativeStockSales,e.RequestedByUserId,e.RequestedByDisplayName,
              e.RedemptionCodeHash,e.ExpiresAt,e.RedeemedAt,
              fs.SeriesId,fs.FiscalAuthorizationId,fs.Prefix,fs.RangeStart,fs.RangeEnd,
              fa.AuthorizationNumber,fa.ValidFrom,fa.ValidUntil,fa.Environment,fa.SupplierTaxId,
              fa.TechnicalKeyVersion,fa.QrValidationUrl
            FROM dbo.PosEnrollmentSessions e WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId AND b.IsActive=1
            JOIN dbo.Warehouses w ON w.WarehouseId=e.WarehouseId AND w.IsActive=1
            LEFT JOIN dbo.FiscalSeries fs WITH (UPDLOCK,HOLDLOCK)
              ON fs.BusinessId=e.BusinessId AND fs.DocumentType=N'SalesInvoice'
             AND fs.EmitterKind=N'Device' AND fs.IsActive=1
             AND ((@ExistingDeviceId IS NOT NULL AND fs.DeviceId=@ExistingDeviceId)
                  OR (@ExistingDeviceId IS NULL AND fs.DeviceId IS NULL))
             AND EXISTS (
                 SELECT 1 FROM dbo.FiscalAuthorizations eligible
                 WHERE eligible.FiscalAuthorizationId=fs.FiscalAuthorizationId
                   AND eligible.BusinessId=e.BusinessId
                   AND eligible.IsActive=1)
            LEFT JOIN dbo.FiscalAuthorizations fa
              ON fa.FiscalAuthorizationId=fs.FiscalAuthorizationId
             AND fa.BusinessId=e.BusinessId AND fa.IsActive=1
            WHERE e.EnrollmentSessionId=@SessionId
            ORDER BY CASE WHEN fs.DeviceId=@ExistingDeviceId THEN 0 ELSE 1 END,
                     fs.CreatedAt,fs.SeriesId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@SessionId", sessionId);
        Add(command, "@ExistingDeviceId", (object?)existingDeviceId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new Provisioning(
            sessionId, reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetBoolean(6), reader.GetGuid(7), reader.GetString(8),
            (byte[])reader[9], reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetInt64(15),
            reader.IsDBNull(16) ? null : reader.GetInt64(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : DateOnly.FromDateTime(reader.GetDateTime(18)),
            reader.IsDBNull(19) ? null : DateOnly.FromDateTime(reader.GetDateTime(19)),
            reader.IsDBNull(20) ? null : reader.GetByte(20),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.IsDBNull(22) ? null : reader.GetString(22),
            reader.IsDBNull(23) ? null : reader.GetString(23));
    }

    private static async Task<string> AllocateSeriesCodeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT SeriesCode
            FROM dbo.DocumentSeries WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND DeviceId IS NOT NULL;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@BusinessId", businessId);
        var used = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) used.Add(reader.GetString(0));
        for (var value = 1; value <= 99; value++)
        {
            var code = value.ToString("00", CultureInfo.InvariantCulture);
            if (!used.Contains(code)) return code;
        }
        throw new PosEnrollmentConflictException(
            "La sede no tiene códigos de serie disponibles para otro dispositivo Edge.");
    }

    private static async Task InsertDeviceAndSeriesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Provisioning data,
        Guid deviceId,
        Guid documentSeriesId,
        string seriesCode,
        PosDeviceCredential credential,
        IReadOnlyCollection<string> permissions,
        string installationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.EnrolledDevices
              (DeviceId,TenantId,Name,CredentialSalt,CredentialHash,
               CredentialIterations,IsActive,CreatedAt)
            VALUES
              (@DeviceId,@TenantId,@Name,@Salt,@Hash,@Iterations,1,@Now);

            INSERT dbo.DocumentSeries
              (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
               Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
            VALUES
              (@DocumentSeriesId,@BusinessId,@DeviceId,N'SalesInvoice',N'VTA',@SeriesCode,
               8,1,99999999,1,1,@Now);

            INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
            VALUES(@DocumentSeriesId,1,@Now);

            UPDATE dbo.PosEnrollmentSessions
            SET RedeemedAt=@Now,DeviceId=@DeviceId
            WHERE EnrollmentSessionId=@SessionId AND RedeemedAt IS NULL;
            IF @@ROWCOUNT<>1 THROW 51002,'The enrollment session was already redeemed.',1;
            """;
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            Add(command, "@DeviceId", deviceId);
            Add(command, "@TenantId", data.TenantId);
            Add(command, "@Name", installationId);
            Add(command, "@Salt", credential.Salt);
            Add(command, "@Hash", credential.Hash);
            Add(command, "@Iterations", credential.Iterations);
            Add(command, "@DocumentSeriesId", documentSeriesId);
            Add(command, "@BusinessId", data.BusinessId);
            Add(command, "@SeriesCode", seriesCode);
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

    private static async Task AssignFiscalSeriesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Provisioning data,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.FiscalSeries
            SET DeviceId=@DeviceId
            WHERE SeriesId=@FiscalSeriesId
              AND EmitterKind=N'Device'
              AND IsActive=1
              AND (DeviceId IS NULL OR DeviceId=@DeviceId);
            IF @@ROWCOUNT<>1
                THROW 51001,'The fiscal device series is no longer available.',1;
            """, connection, transaction);
        Add(command, "@DeviceId", deviceId);
        Add(command, "@FiscalSeriesId", data.FiscalSeriesId!.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PosEnrollmentDocumentSeries> EnsureReceiptDocumentSeriesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid deviceId,
        string seriesCode,
        Guid newSeriesId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DocumentSeriesId,Prefix,SeriesCode,Padding,RangeStart,RangeEnd
            FROM dbo.DocumentSeries WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND DeviceId=@DeviceId
              AND DocumentType=N'SalesReceipt' AND IsActive=1;
            """;
        await using (var read = new SqlCommand(sql, connection, transaction))
        {
            Add(read, "@BusinessId", businessId);
            Add(read, "@DeviceId", deviceId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                return new PosEnrollmentDocumentSeries(
                    reader.GetGuid(0), AuralyDocumentTypes.SalesReceipt,
                    reader.GetString(1), reader.GetString(2), reader.GetByte(3),
                    reader.GetInt64(4), reader.GetInt64(5));
        }

        await using var insert = new SqlCommand("""
            INSERT dbo.DocumentSeries
              (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
               Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
            VALUES
              (@SeriesId,@BusinessId,@DeviceId,N'SalesReceipt',N'CVI',@SeriesCode,
               8,1,99999999,1,1,@Now);
            INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
            VALUES(@SeriesId,1,@Now);
            """, connection, transaction);
        Add(insert, "@SeriesId", newSeriesId);
        Add(insert, "@BusinessId", businessId);
        Add(insert, "@DeviceId", deviceId);
        Add(insert, "@SeriesCode", seriesCode);
        Add(insert, "@Now", now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return new PosEnrollmentDocumentSeries(
            newSeriesId, AuralyDocumentTypes.SalesReceipt, "CVI", seriesCode, 8, 1, 99_999_999);
    }

    private static bool FixedEquals(byte[] left, byte[] right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

    private static SalesWorkspaceContext ReadWorkspace(SqlDataReader reader) =>
        new(
            reader.GetGuid(0), reader.GetString(1),
            reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
            reader.GetBoolean(5));

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    private sealed record ExistingDevice(
        Guid DeviceId,
        Guid TenantId,
        Guid BusinessId,
        Guid DocumentSeriesId,
        string DocumentType,
        string Prefix,
        string SeriesCode,
        byte Padding,
        long RangeStart,
        long RangeEnd);
    private sealed record Provisioning(
        Guid SessionId,
        Guid TenantId,
        Guid BusinessId,
        Guid WarehouseId,
        string BusinessName,
        string WarehouseCode,
        string WarehouseName,
        bool AllowNegativeStock,
        Guid UserId,
        string UserDisplayName,
        byte[] CodeHash,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? RedeemedAt,
        Guid? FiscalSeriesId,
        Guid? FiscalAuthorizationId,
        string? FiscalPrefix,
        long? FiscalRangeStart,
        long? FiscalRangeEnd,
        string? AuthorizationNumber,
        DateOnly? ValidFrom,
        DateOnly? ValidUntil,
        byte? Environment,
        string? SupplierTaxId,
        string? TechnicalKeyVersion,
        string? QrValidationUrl);
}
