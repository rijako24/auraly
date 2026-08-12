using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesInvoiceNumberingConfigurationStore(
    SqlServerConnectionFactory connections) : ISalesInvoiceNumberingConfigurationStore
{
    public async Task<SalesInvoiceNumberingConfiguration> GetAsync(
        Guid tenantId,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(
                SELECT 1 FROM dbo.Businesses
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;

            SELECT n.InitialConsecutive,
                   COALESCE(cursorState.NextConsecutive,n.InitialConsecutive),
                   CONVERT(bit,CASE WHEN EXISTS(
                       SELECT 1 FROM dbo.SalesDocuments d
                       WHERE d.BusinessId=@BusinessId)
                     THEN 1 ELSE 0 END)
            FROM (SELECT 1 Value) source
            LEFT JOIN dbo.SalesInvoiceNumberingConfigurations n
              ON n.BusinessId=@BusinessId
            OUTER APPLY(
                SELECT TOP(1) c.NextConsecutive
                FROM dbo.FiscalSeries s
                JOIN dbo.FiscalSeriesCursors c ON c.SeriesId=s.SeriesId
                WHERE s.BusinessId=@BusinessId
                  AND s.DocumentType=N'SalesInvoice'
                  AND s.EmitterKind=N'Server'
                  AND s.DeviceId IS NULL
                  AND s.IsActive=1
                ORDER BY s.CreatedAt DESC) cursorState;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var initial = reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0);
        var next = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
        var issued = reader.GetBoolean(2);
        return new SalesInvoiceNumberingConfiguration(
            businessId, initial, next, !issued, issued);
    }

    public async Task<SalesInvoiceNumberingConfiguration> SaveAsync(
        Guid tenantId,
        Guid businessId,
        long initialConsecutive,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(
                SELECT 1 FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            IF EXISTS(
                SELECT 1 FROM dbo.SalesDocuments WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId)
                THROW 51022,'El primer consecutivo no puede cambiar porque la sede ya tiene facturas.',1;

            MERGE dbo.SalesInvoiceNumberingConfigurations AS target
            USING (SELECT @BusinessId BusinessId) AS source
              ON target.BusinessId=source.BusinessId
            WHEN MATCHED THEN
              UPDATE SET InitialConsecutive=@InitialConsecutive,UpdatedAt=@Now
            WHEN NOT MATCHED THEN
              INSERT(BusinessId,InitialConsecutive,CreatedAt,UpdatedAt)
              VALUES(@BusinessId,@InitialConsecutive,@Now,@Now);

            DECLARE @AuthorizationId uniqueidentifier,
                    @RangeStart bigint,@RangeEnd bigint,
                    @HasOnline bit=0,@HasOffline bit=0,@Midpoint bigint;
            SELECT TOP(1) @AuthorizationId=FiscalAuthorizationId,
                   @RangeStart=AuthorizedRangeStart,@RangeEnd=AuthorizedRangeEnd
            FROM dbo.FiscalAuthorizations
            WHERE BusinessId=@BusinessId AND IsActive=1
            ORDER BY CreatedAt DESC;

            -- Operational numbering is independent from the DIAN authorization. An incomplete
            -- authorization may already exist while the setup wizard is still collecting its range.
            -- In that case only persist SalesInvoiceNumberingConfigurations; fiscal series are
            -- created later by SqlFiscalConfigurationStore once the resolution is complete.
            IF @AuthorizationId IS NOT NULL AND @RangeStart IS NOT NULL AND @RangeEnd IS NOT NULL
            BEGIN
                IF @InitialConsecutive<@RangeStart OR @InitialConsecutive>@RangeEnd
                    THROW 51022,'El primer consecutivo debe estar dentro del rango DIAN activo.',1;
                IF EXISTS(
                    SELECT 1 FROM dbo.FiscalSeries
                    WHERE FiscalAuthorizationId=@AuthorizationId AND DeviceId IS NOT NULL)
                    THROW 51022,'Desenrola los equipos sin ventas antes de cambiar el primer consecutivo.',1;

                SELECT @HasOnline=CONVERT(bit,MAX(CASE WHEN EmitterKind=N'Server' AND DeviceId IS NULL AND IsActive=1 THEN 1 ELSE 0 END)),
                       @HasOffline=CONVERT(bit,MAX(CASE WHEN EmitterKind=N'Device' AND DeviceId IS NULL AND IsActive=1 THEN 1 ELSE 0 END))
                FROM dbo.FiscalSeries
                WHERE FiscalAuthorizationId=@AuthorizationId AND DocumentType=N'SalesInvoice';
                SET @HasOnline=COALESCE(@HasOnline,0);
                SET @HasOffline=COALESCE(@HasOffline,0);
                SET @Midpoint=@InitialConsecutive+((@RangeEnd-@InitialConsecutive)/2);
                IF @HasOnline=1 AND @HasOffline=1 AND @RangeEnd=@InitialConsecutive
                    THROW 51022,'El rango necesita al menos dos consecutivos para preparar ambos modos.',1;

                UPDATE dbo.FiscalAuthorizations
                SET InitialConsecutive=@InitialConsecutive
                WHERE FiscalAuthorizationId=@AuthorizationId;
                UPDATE dbo.FiscalSeries
                SET RangeStart=@InitialConsecutive,
                    RangeEnd=CASE WHEN @HasOnline=1 AND @HasOffline=1 THEN @Midpoint ELSE @RangeEnd END
                WHERE FiscalAuthorizationId=@AuthorizationId
                  AND EmitterKind=N'Server' AND DeviceId IS NULL AND IsActive=1;
                UPDATE c SET NextConsecutive=@InitialConsecutive,UpdatedAt=@Now
                FROM dbo.FiscalSeriesCursors c
                JOIN dbo.FiscalSeries s ON s.SeriesId=c.SeriesId
                WHERE s.FiscalAuthorizationId=@AuthorizationId
                  AND s.EmitterKind=N'Server' AND s.DeviceId IS NULL AND s.IsActive=1;
                UPDATE dbo.FiscalSeries
                SET RangeStart=CASE WHEN @HasOnline=1 AND @HasOffline=1 THEN @Midpoint+1 ELSE @InitialConsecutive END,
                    RangeEnd=@RangeEnd
                WHERE FiscalAuthorizationId=@AuthorizationId
                  AND EmitterKind=N'Device' AND DeviceId IS NULL AND IsActive=1;
            END;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@InitialConsecutive", initialConsecutive);
        Add(command, "@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(tenantId, businessId, cancellationToken);
    }

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}