using System.Data;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleDocumentHandler
{
    private async Task<Guid> EnsureWorkSessionAsync(
        SqlDocumentProcessingSessionAccessor.Session processing,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        var workSessionId = request.WorkSessionId;
        var session = await FindWorkSessionAsync(
            processing, request.WorkSessionId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        if (session is null)
        {
            await using (var current = new SqlCommand("""
                SELECT TOP(1) WorkSessionId
                FROM dbo.WorkSessions WITH (UPDLOCK,HOLDLOCK)
                WHERE UserId=@UserId AND Status=N'Open'
                ORDER BY OpenedAt DESC;
                """, processing.Connection, processing.Transaction))
            {
                current.Parameters.AddWithValue("@UserId", request.SoldByUserId);
                if (await current.ExecuteScalarAsync(cancellationToken) is Guid activeId)
                {
                    workSessionId = activeId;
                    session = await FindWorkSessionAsync(
                        processing, activeId, cancellationToken);
                }
            }

            if (session is null)
            {
                await using var create = new SqlCommand("""
                    INSERT dbo.WorkSessions
                      (WorkSessionId,BusinessId,WarehouseId,UserId,DeviceId,
                       OpenedAt,LastActivityAt,Status)
                    VALUES
                      (@WorkSessionId,@BusinessId,@WarehouseId,@UserId,@DeviceId,
                       @Now,@Now,N'Open');
                    """, processing.Connection, processing.Transaction);
                AddWorkSessionParameters(create, request, workSessionId, now);
                await create.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (session is not null)
        {
            if (session.UserId != request.SoldByUserId)
            {
                throw new InvalidOperationException(
                    "The work session does not belong to the sale user.");
            }

            if (string.Equals(session.Status, "Open", StringComparison.Ordinal))
            {
                await using var touch = new SqlCommand("""
                    UPDATE dbo.WorkSessions SET LastActivityAt=@Now
                    WHERE WorkSessionId=@WorkSessionId AND Status=N'Open';
                    """, processing.Connection, processing.Transaction);
                touch.Parameters.AddWithValue("@WorkSessionId", workSessionId);
                touch.Parameters.AddWithValue("@Now", now);
                if (await touch.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException(
                        "The work session changed while the sale was being processed.");
            }
            else if (!string.Equals(session.Status, "Closed", StringComparison.Ordinal))
            {
                throw new DBConcurrencyException(
                    "The sale references an invalid historical work session.");
            }
        }

        await using var link = new SqlCommand("""
            UPDATE dbo.SalesDocuments
            SET SoldByUserId=@UserId,WorkSessionId=@WorkSessionId
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId
              AND (WorkSessionId IS NULL OR
                   (WorkSessionId=@WorkSessionId AND SoldByUserId=@UserId));
            """, processing.Connection, processing.Transaction);
        link.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        link.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        link.Parameters.AddWithValue("@UserId", request.SoldByUserId);
        link.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        if (await link.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DBConcurrencyException(
                "The sale could not be linked to its work session.");
        return workSessionId;
    }

    private static async Task<WorkSessionState?> FindWorkSessionAsync(
        SqlDocumentProcessingSessionAccessor.Session processing,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT BusinessId,WarehouseId,UserId,DeviceId,Status
            FROM dbo.WorkSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE WorkSessionId=@WorkSessionId;
            """, processing.Connection, processing.Transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new WorkSessionState(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4));
    }

    private sealed record WorkSessionState(
        Guid BusinessId,
        Guid WarehouseId,
        Guid UserId,
        Guid? DeviceId,
        string Status);

    private static void AddWorkSessionParameters(
        SqlCommand command,
        PosSaleUploadRequest request,
        Guid workSessionId,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@UserId", request.SoldByUserId);
        command.Parameters.AddWithValue(
            "@DeviceId", request.DeviceId == Guid.Empty ? DBNull.Value : request.DeviceId);
        command.Parameters.AddWithValue("@Now", now);
    }
}
