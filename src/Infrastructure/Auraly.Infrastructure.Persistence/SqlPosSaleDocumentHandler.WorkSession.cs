using System.Data;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleDocumentHandler
{
    private async Task ValidateWorkSessionAsync(
        SqlDocumentProcessingSessionAccessor.Session processing,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        var session = await FindWorkSessionAsync(
            processing, request, cancellationToken);
        if (session is null)
            throw new InvalidOperationException(
                "The sale references a work session that does not exist.");

        if (session.UserId != request.SoldByUserId ||
            session.BusinessId != request.BusinessId)
        {
            throw new InvalidOperationException(
                "The sale work session does not match its user or business.");
        }

        if (!string.Equals(session.Status, "Open", StringComparison.Ordinal) &&
            !string.Equals(session.Status, "Closed", StringComparison.Ordinal))
            throw new DBConcurrencyException(
                "The sale references an invalid historical work session.");

        var now = _timeProvider.GetUtcNow();
        if (string.Equals(session.Status, "Open", StringComparison.Ordinal))
        {
            await using var touch = new SqlCommand("""
                UPDATE dbo.WorkSessions SET LastActivityAt=@Now
                WHERE WorkSessionId=@WorkSessionId
                  AND TenantId=@TenantId AND UserId=@UserId
                  AND BusinessId=@BusinessId AND Status=N'Open';
                """, processing.Connection, processing.Transaction);
            touch.Parameters.AddWithValue("@WorkSessionId", request.WorkSessionId);
            touch.Parameters.AddWithValue("@TenantId", request.TenantId);
            touch.Parameters.AddWithValue("@UserId", request.SoldByUserId);
            touch.Parameters.AddWithValue("@BusinessId", request.BusinessId);
            touch.Parameters.AddWithValue("@Now", now);
            if (await touch.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new DBConcurrencyException(
                    "The work session changed while the sale was being processed.");
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
        link.Parameters.AddWithValue("@WorkSessionId", request.WorkSessionId);
        if (await link.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DBConcurrencyException(
                "The sale could not be linked to its work session.");
    }

    private static async Task<WorkSessionState?> FindWorkSessionAsync(
        SqlDocumentProcessingSessionAccessor.Session processing,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT BusinessId,UserId,Status
            FROM dbo.WorkSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE WorkSessionId=@WorkSessionId
              AND TenantId=@TenantId AND UserId=@UserId
              AND BusinessId=@BusinessId;
            """, processing.Connection, processing.Transaction);
        command.Parameters.AddWithValue("@WorkSessionId", request.WorkSessionId);
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@UserId", request.SoldByUserId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new WorkSessionState(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2));
    }

    private sealed record WorkSessionState(
        Guid BusinessId,
        Guid UserId,
        string Status);

}
