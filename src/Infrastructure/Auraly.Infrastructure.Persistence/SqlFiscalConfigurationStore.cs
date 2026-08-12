using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalConfigurationStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    IFiscalTechnicalKeySecretWriter secrets) : IFiscalConfigurationStore
{
    public async Task<FiscalResolutionConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            SELECT TOP(1) a.FiscalAuthorizationId,a.AuthorizationNumber,a.SupplierTaxId,
                a.Environment,a.QrValidationUrl,a.TechnicalKeyVersion,a.ValidFrom,a.ValidUntil,
                COALESCE(online.Prefix,offline.Prefix),
                COALESCE(a.AuthorizedRangeStart,online.RangeStart,offline.RangeStart),
                COALESCE(a.AuthorizedRangeEnd,online.RangeEnd,offline.RangeEnd),
                a.InitialConsecutive,
                CASE WHEN a.InitialConsecutive IS NULL THEN NULL
                     ELSE COALESCE(cursorState.NextConsecutive,a.InitialConsecutive) END,
                CONVERT(bit,CASE WHEN NOT EXISTS(
                    SELECT 1 FROM dbo.SalesDocuments d
                    WHERE d.FiscalAuthorizationId=a.FiscalAuthorizationId)
                  AND NOT EXISTS(
                    SELECT 1 FROM dbo.FiscalSeries assigned
                    WHERE assigned.FiscalAuthorizationId=a.FiscalAuthorizationId
                      AND assigned.DeviceId IS NOT NULL)
                  AND NOT EXISTS(
                    SELECT 1 FROM dbo.FiscalSeriesCursors consumed
                    JOIN dbo.FiscalSeries consumedSeries ON consumedSeries.SeriesId=consumed.SeriesId
                    WHERE consumedSeries.FiscalAuthorizationId=a.FiscalAuthorizationId
                      AND consumed.NextConsecutive>COALESCE(a.InitialConsecutive,consumedSeries.RangeStart))
                  THEN 1 ELSE 0 END),
                CONVERT(bit,CASE WHEN online.SeriesId IS NULL THEN 0 ELSE 1 END),
                CONVERT(bit,CASE WHEN offline.SeriesId IS NULL THEN 0 ELSE 1 END),
                CONVERT(bit,CASE WHEN secret.FiscalTechnicalKeySecretId IS NULL THEN 0 ELSE 1 END)
            FROM dbo.FiscalAuthorizations a
            OUTER APPLY(SELECT TOP(1) s.SeriesId,s.Prefix,s.RangeStart,s.RangeEnd
                FROM dbo.FiscalSeries s WHERE s.BusinessId=a.BusinessId
                  AND s.FiscalAuthorizationId=a.FiscalAuthorizationId
                  AND s.DocumentType=N'SalesInvoice' AND s.EmitterKind=N'Server'
                  AND s.DeviceId IS NULL AND s.IsActive=1 ORDER BY s.CreatedAt DESC) online
            OUTER APPLY(SELECT TOP(1) s.SeriesId,s.Prefix,s.RangeStart,s.RangeEnd
                FROM dbo.FiscalSeries s WHERE s.BusinessId=a.BusinessId
                  AND s.FiscalAuthorizationId=a.FiscalAuthorizationId
                  AND s.DocumentType=N'SalesInvoice' AND s.EmitterKind=N'Device'
                  AND s.DeviceId IS NULL AND s.IsActive=1 ORDER BY s.CreatedAt) offline
            OUTER APPLY(SELECT TOP(1) c.NextConsecutive
                FROM dbo.FiscalSeriesCursors c
                WHERE c.SeriesId=online.SeriesId) cursorState
            OUTER APPLY(SELECT TOP(1) k.FiscalTechnicalKeySecretId
                FROM dbo.FiscalTechnicalKeySecrets k WHERE k.BusinessId=a.BusinessId
                  AND k.FiscalAuthorizationId=a.FiscalAuthorizationId
                  AND k.TechnicalKeyVersion=a.TechnicalKeyVersion
                  AND k.Environment=a.Environment) secret
            WHERE a.BusinessId=@BusinessId AND a.IsActive=1
            ORDER BY a.CreatedAt DESC,a.FiscalAuthorizationId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Empty(businessId);
        var initialConsecutive = reader.IsDBNull(11) ? (long?)null : reader.GetInt64(11);
        var validFrom = DateOnly.FromDateTime(reader.GetDateTime(6));
        var validUntil = DateOnly.FromDateTime(reader.GetDateTime(7));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var isCurrentlyValid = validFrom <= today && today <= validUntil;
        var canSetInitial = reader.GetBoolean(13);
        var online = reader.GetBoolean(14);
        var offline = reader.GetBoolean(15);
        var key = reader.GetBoolean(16);
        var hasConfiguredInitialConsecutive = initialConsecutive.HasValue;
        return new FiscalResolutionConfiguration(
            businessId, reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            reader.GetByte(3), reader.GetString(4), reader.GetString(5),
            validFrom,
            validUntil,
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10),
            initialConsecutive,
            reader.IsDBNull(12) ? null : reader.GetInt64(12),
            canSetInitial, true, online, offline, key,
            online && key && hasConfiguredInitialConsecutive && isCurrentlyValid,
            offline && key && hasConfiguredInitialConsecutive && isCurrentlyValid);
    }

    public async Task<FiscalResolutionConfiguration> SaveAsync(
        Guid tenantId,
        Guid businessId,
        SaveFiscalResolutionConfiguration request,
        CancellationToken cancellationToken)
    {
        var authorizationId = ids.NewId();
        var now = DateTimeOffset.UtcNow;
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;

            DECLARE @AuthorizationExists bit=0,@HasIssued bit=0,
                    @ExistingOnline bit=0,@ExistingOffline bit=0,@ExistingPrefix nvarchar(16);
            SELECT @AuthorizationId=FiscalAuthorizationId,@AuthorizationExists=1
            FROM dbo.FiscalAuthorizations WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND AuthorizationNumber=@AuthorizationNumber;

            IF @AuthorizationExists=1
            BEGIN
                SELECT @ExistingOnline=CONVERT(bit,MAX(CASE WHEN EmitterKind=N'Server' AND DeviceId IS NULL AND IsActive=1 THEN 1 ELSE 0 END)),
                       @ExistingOffline=CONVERT(bit,MAX(CASE WHEN EmitterKind=N'Device' AND IsActive=1 THEN 1 ELSE 0 END)),
                       @ExistingPrefix=MAX(CASE WHEN IsActive=1 THEN Prefix END)
                FROM dbo.FiscalSeries
                WHERE FiscalAuthorizationId=@AuthorizationId AND DocumentType=N'SalesInvoice';
                SET @ExistingOnline=COALESCE(@ExistingOnline,0);
                SET @ExistingOffline=COALESCE(@ExistingOffline,0);
                IF EXISTS(SELECT 1 FROM dbo.SalesDocuments WHERE FiscalAuthorizationId=@AuthorizationId)
                   OR EXISTS(SELECT 1 FROM dbo.FiscalSeries WHERE FiscalAuthorizationId=@AuthorizationId AND DeviceId IS NOT NULL)
                   OR EXISTS(SELECT 1 FROM dbo.FiscalSeriesCursors c
                             JOIN dbo.FiscalSeries s ON s.SeriesId=c.SeriesId
                             WHERE s.FiscalAuthorizationId=@AuthorizationId
                               AND c.NextConsecutive>COALESCE((SELECT InitialConsecutive FROM dbo.FiscalAuthorizations WHERE FiscalAuthorizationId=@AuthorizationId),0))
                    SET @HasIssued=1;

                IF @HasIssued=1 AND EXISTS(
                    SELECT 1 FROM dbo.FiscalAuthorizations a
                    WHERE a.FiscalAuthorizationId=@AuthorizationId
                      AND (COALESCE(a.AuthorizedRangeStart,@RangeStart)<>@RangeStart
                        OR COALESCE(a.AuthorizedRangeEnd,@RangeEnd)<>@RangeEnd
                        OR COALESCE(a.InitialConsecutive,@InitialConsecutive)<>@InitialConsecutive
                        OR COALESCE(@ExistingPrefix,@Prefix)<>@Prefix
                        OR @ExistingOnline<>@PrepareOnline
                        OR @ExistingOffline<>@PrepareOffline))
                    THROW 51022,'La numeración no puede reiniciarse ni cambiar después de emitir una factura o enrolar un equipo.',1;
            END;

            UPDATE dbo.FiscalAuthorizations SET IsActive=0
            WHERE BusinessId=@BusinessId AND FiscalAuthorizationId<>@AuthorizationId;
            IF @AuthorizationExists=1
                UPDATE dbo.FiscalAuthorizations
                SET SupplierTaxId=@SupplierTaxId,Environment=@Environment,
                    QrValidationUrl=@QrUrl,TechnicalKeyVersion=@Version,ValidFrom=@ValidFrom,
                    ValidUntil=@ValidUntil,AuthorizedRangeStart=@RangeStart,
                    AuthorizedRangeEnd=@RangeEnd,InitialConsecutive=@InitialConsecutive,IsActive=1
                WHERE FiscalAuthorizationId=@AuthorizationId;
            ELSE
                INSERT dbo.FiscalAuthorizations(
                    FiscalAuthorizationId,BusinessId,AuthorizationNumber,SupplierTaxId,Environment,
                    QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,AuthorizedRangeStart,
                    AuthorizedRangeEnd,InitialConsecutive,IsActive,CreatedAt)
                VALUES(@AuthorizationId,@BusinessId,@AuthorizationNumber,@SupplierTaxId,@Environment,
                       @QrUrl,@Version,@ValidFrom,@ValidUntil,@RangeStart,@RangeEnd,
                       @InitialConsecutive,1,@Now);

            IF @HasIssued=0
            BEGIN
                DELETE c FROM dbo.FiscalSeriesCursors c
                JOIN dbo.FiscalSeries s ON s.SeriesId=c.SeriesId
                WHERE s.BusinessId=@BusinessId AND s.FiscalAuthorizationId=@AuthorizationId
                  AND s.DeviceId IS NULL;
                UPDATE dbo.FiscalSeries SET IsActive=0
                WHERE BusinessId=@BusinessId AND DocumentType=N'SalesInvoice'
                  AND FiscalAuthorizationId=@AuthorizationId AND DeviceId IS NULL;

                IF @PrepareOnline=1
                BEGIN
                    INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                        DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
                    VALUES(@OnlineSeriesId,@BusinessId,NULL,N'Server',@AuthorizationId,
                        N'SalesInvoice',@Prefix,@InitialConsecutive,@OnlineRangeEnd,1,@Now);
                    INSERT dbo.FiscalSeriesCursors(SeriesId,NextConsecutive,UpdatedAt)
                    VALUES(@OnlineSeriesId,@InitialConsecutive,@Now);
                END;
                IF @PrepareOffline=1
                    INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                        DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
                    VALUES(@OfflineSeriesId,@BusinessId,NULL,N'Device',@AuthorizationId,
                        N'SalesInvoice',@Prefix,@OfflineRangeStart,@RangeEnd,1,@Now);
            END;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            var authorization = command.Parameters.Add("@AuthorizationId", System.Data.SqlDbType.UniqueIdentifier);
            authorization.Direction = System.Data.ParameterDirection.InputOutput;
            authorization.Value = authorizationId;
            Add(command, "@TenantId", tenantId);
            Add(command, "@BusinessId", businessId);
            Add(command, "@AuthorizationNumber", request.AuthorizationNumber.Trim());
            Add(command, "@SupplierTaxId", request.SupplierTaxId.Trim());
            Add(command, "@Environment", request.Environment);
            Add(command, "@QrUrl", request.QrValidationUrl.Trim());
            Add(command, "@Version", request.TechnicalKeyVersion.Trim());
            Add(command, "@ValidFrom", request.ValidFrom.ToDateTime(TimeOnly.MinValue));
            Add(command, "@ValidUntil", request.ValidUntil.ToDateTime(TimeOnly.MinValue));
            Add(command, "@Prefix", request.Prefix.Trim().ToUpperInvariant());
            Add(command, "@RangeStart", request.RangeStart);
            Add(command, "@RangeEnd", request.RangeEnd);
            Add(command, "@InitialConsecutive", request.InitialConsecutive);
            var midpoint = request.InitialConsecutive + ((request.RangeEnd - request.InitialConsecutive) / 2);
            Add(command, "@OnlineRangeEnd", request.PrepareOnlineSeries && request.PrepareOfflineSeries ? midpoint : request.RangeEnd);
            Add(command, "@OfflineRangeStart", request.PrepareOnlineSeries && request.PrepareOfflineSeries ? midpoint + 1 : request.InitialConsecutive);
            Add(command, "@PrepareOnline", request.PrepareOnlineSeries);
            Add(command, "@PrepareOffline", request.PrepareOfflineSeries);
            Add(command, "@OnlineSeriesId", ids.NewId());
            Add(command, "@OfflineSeriesId", ids.NewId());
            Add(command, "@Now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
            authorizationId = (Guid)authorization.Value;
        }
        await transaction.CommitAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.TechnicalKey))
            await secrets.SaveAsync(
                tenantId, businessId, authorizationId, request.AuthorizationNumber,
                request.TechnicalKeyVersion, request.Environment, request.SupplierTaxId,
                request.QrValidationUrl, request.TechnicalKey, cancellationToken);
        return await GetAsync(tenantId, businessId, cancellationToken);
    }

    private static FiscalResolutionConfiguration Empty(Guid businessId) =>
        new(businessId, null, null, null, 2, null, null, null, null, null, null, null,
            null, null, true, false, false, false, false, false, false);

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
