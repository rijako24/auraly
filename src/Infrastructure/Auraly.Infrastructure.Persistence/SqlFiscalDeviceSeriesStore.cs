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
                ORDER BY CASE s.AllocationState WHEN N'Active' THEN 0 ELSE 1 END,
                         s.CreatedAt DESC,s.SeriesId
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
        await EnsureProvisioningAsync(
            tenantId, businessId, request.DeviceId, null, null,
            cancellationToken);
        return await ListAsync(tenantId, businessId, cancellationToken);
    }

    public Task<IReadOnlyList<PosFiscalSeriesProvisioning>> GetProvisioningAsync(
        Guid tenantId, Guid businessId, Guid deviceId, Guid? currentSeriesId,
        long? nextConsecutive,
        CancellationToken cancellationToken)
        => EnsureProvisioningAsync(
            tenantId, businessId, deviceId, currentSeriesId, nextConsecutive,
            cancellationToken);

    private async Task<IReadOnlyList<PosFiscalSeriesProvisioning>> EnsureProvisioningAsync(
        Guid tenantId, Guid businessId, Guid deviceId, Guid? currentSeriesId,
        long? nextConsecutive, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        await using var command = new SqlCommand(
            "fiscal.FiscalDeviceNumberingEnsure", connection, transaction)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@DeviceId", deviceId);
        command.Parameters.AddWithValue("@CurrentSeriesId", (object?)currentSeriesId ?? DBNull.Value);
        command.Parameters.AddWithValue("@NextConsecutive", (object?)nextConsecutive ?? DBNull.Value);
        Add(command, "@ActiveSeriesId", ids.NewId());
        Add(command, "@StandbySeriesId", ids.NewId());
        Add(command, "@ActiveNotificationId", ids.NewId());
        Add(command, "@StandbyNotificationId", ids.NewId());
        Add(command, "@Now", DateTimeOffset.UtcNow);

        var rows = new List<ProvisioningRow>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new ProvisioningRow(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5),
                    DateOnly.FromDateTime(reader.GetDateTime(6)),
                    DateOnly.FromDateTime(reader.GetDateTime(7)), reader.GetByte(8),
                    reader.GetString(9), reader.GetString(10), reader.GetString(11),
                    reader.GetString(12), reader.GetInt64(13), reader.GetInt64(14)));
        }
        catch (SqlException exception) when (exception.Number is 51023 or 51027)
        {
            throw new FiscalConfigurationValidationException(exception.Message);
        }
        await transaction.CommitAsync(cancellationToken);

        var result = new List<PosFiscalSeriesProvisioning>(rows.Count);
        foreach (var row in rows)
        {
            var material = await technicalKeys.ResolveAsync(
                new FiscalKeyReference(tenantId, businessId, row.AuthorizationNumber,
                    row.TechnicalKeyVersion, (FiscalEnvironment)row.Environment),
                cancellationToken)
                ?? throw new FiscalConfigurationValidationException(
                    "La clave técnica de la resolución asignada no está disponible.");
            result.Add(new PosFiscalSeriesProvisioning(
                row.SeriesId, row.FiscalAuthorizationId, row.Prefix,
                row.AuthorizationNumber, row.RangeStart, row.RangeEnd,
                row.ValidUntil, row.Environment, material.SupplierTaxId,
                new string(material.TechnicalKey.Reveal()), row.TechnicalKeyVersion,
                material.QrValidationUrl, row.ValidFrom, row.AllocationState,
                row.AuthorizationRangeStart,
                row.AuthorizationRangeEnd));
        }
        return result;
    }

    private sealed record ProvisioningRow(
        Guid SeriesId, Guid FiscalAuthorizationId, string Prefix,
        string AuthorizationNumber, long RangeStart, long RangeEnd,
        DateOnly ValidFrom, DateOnly ValidUntil, byte Environment,
        string SupplierTaxId, string TechnicalKeyVersion, string QrValidationUrl,
        string AllocationState,
        long AuthorizationRangeStart, long AuthorizationRangeEnd);

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
