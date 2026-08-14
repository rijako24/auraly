using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantDeviceAdminStore(ApplicationDbContext db) : ITenantDeviceAdminStore
{
    public async Task<IReadOnlyList<TenantEnrolledDeviceDto>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);
        const string sql = """
            SELECT d.DeviceId,d.Name,d.IsActive,d.CreatedAt,d.LastSeenAt,scope.BusinessId,b.Name
            FROM dbo.EnrolledDevices d
            OUTER APPLY (
              SELECT TOP(1) ds.BusinessId
              FROM dbo.DocumentSeries ds
              WHERE ds.DeviceId=d.DeviceId
              ORDER BY ds.IsActive DESC,ds.CreatedAt DESC,ds.DocumentSeriesId) scope
            LEFT JOIN dbo.Businesses b ON b.BusinessId=scope.BusinessId
            WHERE d.TenantId=@TenantId
            ORDER BY d.IsActive DESC,COALESCE(d.LastSeenAt,d.CreatedAt) DESC,d.Name;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        var devices = new List<TenantEnrolledDeviceDto>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            devices.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2), reader.GetDateTimeOffset(3), reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return devices;
    }

    public async Task DeactivateAsync(Guid tenantId, Guid deviceId, CancellationToken ct = default)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            const string sql = """
                DECLARE @Now DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();
                UPDATE dbo.EnrolledDevices SET IsActive=0 WHERE DeviceId=@DeviceId AND TenantId=@TenantId AND IsActive=1;
                IF @@ROWCOUNT<>1 THROW 51090,N'La caja no existe, ya fue desenrolada o pertenece a otra organización.',1;
                UPDATE dbo.DocumentSeries SET IsActive=0 WHERE DeviceId=@DeviceId AND IsActive=1;
                UPDATE dbo.FiscalSeries SET IsActive=0 WHERE DeviceId=@DeviceId AND IsActive=1;
                UPDATE dbo.WorkSessions SET Status=N'Closed',ClosedAt=@Now,LastActivityAt=@Now WHERE DeviceId=@DeviceId AND Status=N'Open';
                UPDATE dbo.OfflineAuthenticationLeases
                SET Status=N'Revoked',EndedAt=@Now,EndReason=N'DeviceUnenrolled',UpdatedAt=@Now
                WHERE DeviceId=@DeviceId AND Status=N'Active';
                """;
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", tenantId);
            command.Parameters.AddWithValue("@DeviceId", deviceId);
            await command.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException exception) when (exception.Number == 51090)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new NotFoundException("EnrolledDevice", deviceId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}