using System.Data;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleDocumentHandler
{
    private async Task<CashResponsibility> EnsureCashResponsibilityAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken ct)
    {
        Guid cashSessionId;
        Guid cashierShiftId;
        var cashSessionExists = false;
        var cashierShiftExists = false;
        await using (var registerLock = new SqlCommand("""
            SELECT RegisterId
            FROM dbo.CashRegisters WITH (UPDLOCK,HOLDLOCK)
            WHERE RegisterId=@RegisterId;
            """, session.Connection, session.Transaction))
        {
            registerLock.Parameters.AddWithValue("@RegisterId", request.RegisterId);
            if (await registerLock.ExecuteScalarAsync(ct) is not Guid)
                throw new DBConcurrencyException(
                    "The sale register does not exist.");
        }

        await using (var current = new SqlCommand("""
            SELECT cs.CashSessionId,sh.CashierShiftId
            FROM dbo.CashSessions cs WITH (UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.CashierShifts sh WITH (UPDLOCK,HOLDLOCK)
              ON sh.CashSessionId=cs.CashSessionId
             AND sh.Status=N'Active' AND sh.UserId=@UserId
            WHERE cs.RegisterId=@RegisterId AND cs.Status=N'Open';
            """, session.Connection, session.Transaction))
        {
            current.Parameters.AddWithValue("@RegisterId", request.RegisterId);
            current.Parameters.AddWithValue("@UserId", request.SoldByUserId);
            await using var reader = await current.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                cashSessionExists = true;
                cashSessionId = reader.GetGuid(0);
                cashierShiftExists = !reader.IsDBNull(1);
                cashierShiftId = cashierShiftExists
                    ? reader.GetGuid(1)
                    : _idGenerator.NewId();
            }
            else
            {
                cashSessionId = _idGenerator.NewId();
                cashierShiftId = _idGenerator.NewId();
            }
        }

        var now = _timeProvider.GetUtcNow();
        if (!cashierShiftExists)
        {
            var sql = cashSessionExists
                ? """
                  INSERT dbo.CashierShifts
                    (CashierShiftId,CashSessionId,RegisterId,UserId,StartedAt,Status)
                  VALUES
                    (@ShiftId,@SessionId,@RegisterId,@UserId,@Now,N'Active');
                  """
                : """
                  INSERT dbo.CashSessions
                    (CashSessionId,BusinessId,LocationId,RegisterId,OpenedByUserId,
                     OpenedAt,OpeningFloat,Status,OpenIdempotencyKey)
                  VALUES
                    (@SessionId,@BusinessId,@LocationId,@RegisterId,@UserId,
                     @Now,0,N'Open',@OpenKey);
                  INSERT dbo.CashierShifts
                    (CashierShiftId,CashSessionId,RegisterId,UserId,StartedAt,Status)
                  VALUES
                    (@ShiftId,@SessionId,@RegisterId,@UserId,@Now,N'Active');
                  """;
            await using var create = new SqlCommand(
                sql, session.Connection, session.Transaction);
            AddResponsibilityParameters(
                create, request, cashSessionId, cashierShiftId, now);
            if (!cashSessionExists)
                create.Parameters.AddWithValue(
                    "@OpenKey", $"sale:{request.DocumentId:D}");
            await create.ExecuteNonQueryAsync(ct);
        }

        await using var link = new SqlCommand("""
            UPDATE dbo.SalesDocuments
            SET SoldByUserId=@UserId,CashSessionId=@SessionId,CashierShiftId=@ShiftId
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId
              AND (CashSessionId IS NULL OR
                   (CashSessionId=@SessionId AND CashierShiftId=@ShiftId
                    AND SoldByUserId=@UserId));
            """, session.Connection, session.Transaction);
        link.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        link.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        link.Parameters.AddWithValue("@UserId", request.SoldByUserId);
        link.Parameters.AddWithValue("@SessionId", cashSessionId);
        link.Parameters.AddWithValue("@ShiftId", cashierShiftId);
        if (await link.ExecuteNonQueryAsync(ct) != 1)
            throw new DBConcurrencyException(
                "The sale could not be linked to its cashier responsibility.");
        return new CashResponsibility(cashSessionId, cashierShiftId);
    }

    private async Task InsertCashMovementAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        PosSalePaymentContract payment,
        CashResponsibility responsibility,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.CashMovements
              (CashMovementId,CashSessionId,CashierShiftId,DocumentId,PaymentNumber,
               BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,SourceKey,
               OccurredAt,RecordedByUserId)
            VALUES
              (@MovementId,@SessionId,@ShiftId,@DocumentId,@PaymentNumber,
               @BusinessDate,N'SalePayment',@Method,@Amount,@Reference,@SourceKey,
               @OccurredAt,@UserId);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MovementId", _idGenerator.NewId());
        command.Parameters.AddWithValue("@SessionId", responsibility.CashSessionId);
        command.Parameters.AddWithValue("@ShiftId", responsibility.CashierShiftId);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@PaymentNumber", payment.PaymentNumber);
        command.Parameters.Add(
            new SqlParameter("@BusinessDate", SqlDbType.Date)
            {
                Value = request.FiscalSnapshot.IssuedAt.Date
            });
        command.Parameters.AddWithValue("@Method", payment.MethodCode);
        AddDecimal(command, "@Amount", payment.Amount, 19, 4);
        command.Parameters.AddWithValue(
            "@Reference", (object?)payment.Reference ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@SourceKey", $"sale:{request.DocumentId:D}:{payment.PaymentNumber}");
        command.Parameters.AddWithValue("@OccurredAt", request.FiscalSnapshot.IssuedAt);
        command.Parameters.AddWithValue("@UserId", request.SoldByUserId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddResponsibilityParameters(
        SqlCommand command,
        PosSaleUploadRequest request,
        Guid sessionId,
        Guid shiftId,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@ShiftId", shiftId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@LocationId", request.LocationId);
        command.Parameters.AddWithValue("@RegisterId", request.RegisterId);
        command.Parameters.AddWithValue("@UserId", request.SoldByUserId);
        command.Parameters.AddWithValue("@Now", now);
    }

    private sealed record CashResponsibility(Guid CashSessionId, Guid CashierShiftId);
}
