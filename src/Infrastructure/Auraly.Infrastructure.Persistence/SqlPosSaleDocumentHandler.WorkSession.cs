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
        await using (var current = new SqlCommand("""
            SELECT WorkSessionId,BusinessId,WarehouseId,DeviceId
            FROM dbo.WorkSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE UserId=@UserId AND Status=N'Open';
            """, processing.Connection, processing.Transaction))
        {
            current.Parameters.AddWithValue("@UserId", request.SoldByUserId);
            await using var reader = await current.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingBusinessId = reader.GetGuid(1);
                var existingWarehouseId = reader.GetGuid(2);
                var existingDeviceId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
                var requestedDeviceId = request.DeviceId == Guid.Empty ? (Guid?)null : request.DeviceId;
                if (reader.GetGuid(0) != request.WorkSessionId ||
                    existingBusinessId != request.BusinessId ||
                    existingWarehouseId != request.WarehouseId ||
                    existingDeviceId != requestedDeviceId)
                    throw new InvalidOperationException(
                        "The user already has an open work session in another context.");
            }
        }

        var now = _timeProvider.GetUtcNow();
        if (!await WorkSessionExistsAsync(
                processing, request.WorkSessionId, cancellationToken))
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
        else
        {
            await using var touch = new SqlCommand("""
                UPDATE dbo.WorkSessions SET LastActivityAt=@Now
                WHERE WorkSessionId=@WorkSessionId AND Status=N'Open';
                """, processing.Connection, processing.Transaction);
            touch.Parameters.AddWithValue("@WorkSessionId", workSessionId);
            touch.Parameters.AddWithValue("@Now", now);
            if (await touch.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new DBConcurrencyException("The work session is no longer open.");
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

    private static async Task<bool> WorkSessionExistsAsync(
        SqlDocumentProcessingSessionAccessor.Session processing,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT_BIG(1) FROM dbo.WorkSessions WHERE WorkSessionId=@WorkSessionId;",
            processing.Connection,
            processing.Transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L) == 1;
    }
    private async Task InsertWorkSessionMovementAsync(
        SqlDocumentProcessingSessionAccessor.Session processing,
        PosSaleUploadRequest request,
        PosSalePaymentContract payment,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.WorkSessionMovements
              (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,
               BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,SourceKey,
               OccurredAt,RecordedByUserId)
            VALUES
              (@MovementId,@WorkSessionId,@DocumentId,@PaymentNumber,
               @BusinessDate,N'SalePayment',@Method,@Amount,@Reference,@SourceKey,
               @OccurredAt,@UserId);
            """, processing.Connection, processing.Transaction);
        command.Parameters.AddWithValue("@MovementId", _idGenerator.NewId());
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@PaymentNumber", payment.PaymentNumber);
        command.Parameters.Add(new SqlParameter("@BusinessDate", SqlDbType.Date)
        {
            Value = request.CommercialSnapshot.IssuedAt.Date
        });
        command.Parameters.AddWithValue("@Method", payment.MethodCode);
        AddDecimal(command, "@Amount", payment.Amount, 19, 4);
        command.Parameters.AddWithValue("@Reference", (object?)payment.Reference ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@SourceKey", $"sale:{request.DocumentId:D}:{payment.PaymentNumber}");
        command.Parameters.AddWithValue("@OccurredAt", request.CommercialSnapshot.IssuedAt);
        command.Parameters.AddWithValue("@UserId", request.SoldByUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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
