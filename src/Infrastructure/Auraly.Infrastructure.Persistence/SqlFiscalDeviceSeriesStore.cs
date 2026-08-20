using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalDeviceSeriesStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    IFiscalTechnicalKeyProvider technicalKeys) : IFiscalDeviceSeriesStore
{
    public async Task<FiscalDeviceSeriesWorkspace> ListAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,N'La sede no pertenece a la empresa autenticada.',1;

            SELECT COALESCE(SUM(RangeEnd-RangeStart+1),0)
            FROM dbo.FiscalSeries available
            JOIN dbo.FiscalAuthorizations auth
              ON auth.FiscalAuthorizationId=available.FiscalAuthorizationId
            WHERE available.BusinessId=@BusinessId AND available.EmitterKind=N'Device'
              AND available.DeviceId IS NULL AND available.IsActive=1
              AND auth.IsActive=1
              AND CONVERT(date,SYSUTCDATETIME())<=auth.ValidUntil;

            SELECT d.DeviceId,d.Name,d.IsActive,d.LastSeenAt,b.BusinessId,b.Name,
                   fs.SeriesId,fs.Prefix,fs.RangeStart,fs.RangeEnd
            FROM dbo.EnrolledDevices d
            JOIN (
                SELECT DeviceId,BusinessId,
                       ROW_NUMBER() OVER(PARTITION BY DeviceId ORDER BY IsActive DESC,CreatedAt DESC,DocumentSeriesId) Position
                FROM dbo.DocumentSeries WHERE DeviceId IS NOT NULL
            ) scope ON scope.DeviceId=d.DeviceId AND scope.Position=1
            JOIN dbo.Businesses b ON b.BusinessId=scope.BusinessId
            OUTER APPLY(
                SELECT TOP(1) s.SeriesId,s.Prefix,s.RangeStart,s.RangeEnd
                FROM dbo.FiscalSeries s
                JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
                WHERE s.BusinessId=@BusinessId AND s.DeviceId=d.DeviceId
                  AND s.EmitterKind=N'Device' AND s.DocumentType=N'SalesInvoice'
                  AND s.IsActive=1 AND a.IsActive=1
                  AND CONVERT(date,SYSUTCDATETIME())<=a.ValidUntil
                  AND COALESCE((SELECT MAX(document.FiscalConsecutive)
                                FROM dbo.SalesDocuments document
                                WHERE document.FiscalSeriesId=s.SeriesId),s.RangeStart-1)<s.RangeEnd
                ORDER BY s.CreatedAt DESC,s.SeriesId
            ) fs
            WHERE d.TenantId=@TenantId AND scope.BusinessId=@BusinessId
            ORDER BY d.IsActive DESC,d.Name,d.DeviceId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var available = reader.GetInt64(0);
        await reader.NextResultAsync(cancellationToken);
        var devices = new List<FiscalDeviceSeriesAssignment>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var seriesId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6);
            devices.Add(new FiscalDeviceSeriesAssignment(
                reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3),
                reader.GetGuid(4), reader.GetString(5), seriesId,
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                seriesId.HasValue));
        }
        return new FiscalDeviceSeriesWorkspace(businessId, available, devices);
    }

    public async Task<FiscalDeviceSeriesWorkspace> AssignAsync(
        Guid tenantId, Guid businessId, AssignFiscalDeviceSeriesRequest request,
        CancellationToken cancellationToken)
    {
        var seriesId = ids.NewId();
        var notificationId = ids.NewId();
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(
                SELECT 1 FROM dbo.EnrolledDevices d
                JOIN dbo.DocumentSeries ds ON ds.DeviceId=d.DeviceId
                JOIN dbo.Businesses b ON b.BusinessId=ds.BusinessId
                WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1
                  AND ds.BusinessId=@BusinessId AND ds.IsActive=1 AND b.IsActive=1)
                THROW 51023,N'El equipo no está enrolado y activo en la sede seleccionada.',1;
            IF EXISTS(
                SELECT 1 FROM dbo.FiscalSeries existing
                JOIN dbo.FiscalAuthorizations existingAuthorization
                  ON existingAuthorization.FiscalAuthorizationId=existing.FiscalAuthorizationId
                WHERE existing.DeviceId=@DeviceId AND existing.IsActive=1
                  AND existing.DocumentType=N'SalesInvoice'
                  AND CONVERT(date,@Now)<=existingAuthorization.ValidUntil
                  AND COALESCE((SELECT MAX(document.FiscalConsecutive)
                                FROM dbo.SalesDocuments document
                                WHERE document.FiscalSeriesId=existing.SeriesId),existing.RangeStart-1)<existing.RangeEnd)
                THROW 51024,N'El equipo todavía tiene una serie fiscal vigente con numeración disponible.',1;
            UPDATE dbo.FiscalSeries SET IsActive=0
            WHERE DeviceId=@DeviceId AND IsActive=1 AND DocumentType=N'SalesInvoice';

            DECLARE @PoolId uniqueidentifier,@AuthorizationId uniqueidentifier,
                    @Prefix nvarchar(16),@Start bigint,@PoolEnd bigint,@AssignedEnd bigint;
            SELECT TOP(1) @PoolId=s.SeriesId,@AuthorizationId=s.FiscalAuthorizationId,
                   @Prefix=s.Prefix,@Start=s.RangeStart,@PoolEnd=s.RangeEnd
            FROM dbo.FiscalSeries s WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.FiscalAuthorizations a WITH(UPDLOCK,HOLDLOCK)
              ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
            WHERE s.BusinessId=@BusinessId AND s.EmitterKind=N'Device'
              AND s.DeviceId IS NULL AND s.DocumentType=N'SalesInvoice'
              AND s.IsActive=1 AND a.IsActive=1
              AND CONVERT(date,@Now)<=a.ValidUntil
            ORDER BY s.RangeStart,s.SeriesId;
            IF @PoolId IS NULL THROW 51025,N'La resolución no tiene numeración disponible para cajas desconectadas.',1;
            SET @AssignedEnd=@Start+@Count-1;
            IF @AssignedEnd>@PoolEnd OR @AssignedEnd<@Start
                THROW 51026,N'La numeración disponible no alcanza para la cantidad solicitada.',1;

            INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES(@SeriesId,@BusinessId,@DeviceId,N'Device',@AuthorizationId,
                N'SalesInvoice',@Prefix,@Start,@AssignedEnd,1,@Now);
            IF @AssignedEnd=@PoolEnd
                UPDATE dbo.FiscalSeries SET IsActive=0 WHERE SeriesId=@PoolId;
            ELSE
                UPDATE dbo.FiscalSeries SET RangeStart=@AssignedEnd+1 WHERE SeriesId=@PoolId;

            INSERT dbo.PosSynchronizationOutboxMessages
                (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            VALUES(@NotificationId,@BusinessId,N'FiscalProvisioning',@Start,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            Add(command, "@TenantId", tenantId);
            Add(command, "@BusinessId", businessId);
            Add(command, "@DeviceId", request.DeviceId);
            Add(command, "@Count", request.ConsecutiveCount);
            Add(command, "@SeriesId", seriesId);
            Add(command, "@NotificationId", notificationId);
            Add(command, "@Now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 51023 or 51024 or 51025 or 51026)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new FiscalConfigurationValidationException(exception.Message);
        }
        return await ListAsync(tenantId, businessId, cancellationToken);
    }

    public async Task<PosFiscalSeriesProvisioning?> GetProvisioningAsync(
        Guid tenantId, Guid businessId, Guid deviceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(1) s.SeriesId,s.FiscalAuthorizationId,s.Prefix,
                a.AuthorizationNumber,s.RangeStart,s.RangeEnd,a.ValidFrom,a.ValidUntil,
                a.Environment,a.SupplierTaxId,a.TechnicalKeyVersion,a.QrValidationUrl
            FROM dbo.EnrolledDevices d
            JOIN dbo.DocumentSeries ds ON ds.DeviceId=d.DeviceId AND ds.BusinessId=@BusinessId AND ds.IsActive=1
            JOIN dbo.FiscalSeries s ON s.DeviceId=d.DeviceId AND s.BusinessId=ds.BusinessId
            JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
            WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1
              AND s.EmitterKind=N'Device' AND s.DocumentType=N'SalesInvoice'
              AND s.IsActive=1 AND a.IsActive=1
            ORDER BY s.CreatedAt DESC,s.SeriesId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@DeviceId", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var authorizationNumber = reader.GetString(3);
        var environment = reader.GetByte(8);
        var version = reader.GetString(10);
        var material = await technicalKeys.ResolveAsync(
            new FiscalKeyReference(tenantId, businessId, authorizationNumber,
                version, (FiscalEnvironment)environment), cancellationToken)
            ?? throw new FiscalConfigurationValidationException(
                "La clave técnica de la resolución asignada no está disponible.");
        return new PosFiscalSeriesProvisioning(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            authorizationNumber, reader.GetInt64(4), reader.GetInt64(5),
            DateOnly.FromDateTime(reader.GetDateTime(7)), environment,
            material.SupplierTaxId, new string(material.TechnicalKey.Reveal()),
            version, material.QrValidationUrl,
            DateOnly.FromDateTime(reader.GetDateTime(6)));
    }

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
