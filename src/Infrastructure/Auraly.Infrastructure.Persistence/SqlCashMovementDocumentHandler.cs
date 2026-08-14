using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlCashReceiptDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
    : SqlCashMovementDocumentHandler(
        CashMovementDocumentTypes.Receipt, sessions, ids, timeProvider);

public sealed class SqlCashDisbursementDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
    : SqlCashMovementDocumentHandler(
        CashMovementDocumentTypes.Disbursement, sessions, ids, timeProvider);

public abstract class SqlCashMovementDocumentHandler(
    string documentType,
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => documentType;

    public async Task HandleAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var movement = CashMovementContractSerializer.Deserialize(document.Payload);
        var expectedType = CashMovementDocumentTypes.FromDirection(movement.Direction);
        if (movement.DocumentId != document.DocumentId.Value ||
            movement.BusinessId != document.BusinessId.Value ||
            movement.TenantId != document.TenantId.Value ||
            expectedType != document.DocumentType)
            throw new InvalidOperationException(
                "The cash movement envelope does not match its immutable payload.");

        var session = sessions.Current;
        await LockAcceptedDocumentAsync(session, movement, cancellationToken);
        await InsertDrawerMovementAsync(session, movement, cancellationToken);
        await CompleteDocumentAsync(session, movement, cancellationToken);
        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, movement.OccurredAt, ids, timeProvider,
            cancellationToken);
        await InsertOutboxAsync(
            session, movement, document.Payload, cancellationToken);
    }

    private static async Task LockAcceptedDocumentAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        CashMovementDocumentPayload movement,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT WorkSessionId,ReasonId,Direction,Amount,OccurredAt,
                   ConfirmedByUserId,Status
            FROM dbo.CashMovementDocuments WITH (UPDLOCK,HOLDLOCK)
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId
              AND DocumentType=@DocumentType;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", movement.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", movement.BusinessId);
        command.Parameters.AddWithValue(
            "@DocumentType",
            CashMovementDocumentTypes.FromDirection(movement.Direction));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            reader.GetGuid(0) != movement.WorkSessionId ||
            reader.GetGuid(1) != movement.ReasonId ||
            reader.GetString(2) != movement.Direction ||
            reader.GetDecimal(3) != movement.Amount ||
            reader.GetDateTimeOffset(4) != movement.OccurredAt ||
            reader.GetGuid(5) != movement.ConfirmedByUserId ||
            reader.GetString(6) != "Accepted")
            throw new InvalidOperationException(
                "The accepted cash movement no longer matches its immutable payload.");
    }

    private async Task InsertDrawerMovementAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        CashMovementDocumentPayload movement,
        CancellationToken cancellationToken)
    {
        var isCashIn = movement.Direction == CashMovementDirections.In;
        await using var command = new SqlCommand("""
            INSERT dbo.WorkSessionMovements
              (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,
               BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,
               SourceKey,OccurredAt,RecordedByUserId)
            VALUES
              (@MovementId,@WorkSessionId,@DocumentId,NULL,@BusinessDate,
               @MovementType,N'Cash',@Amount,@Reference,@SourceKey,@OccurredAt,@UserId);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MovementId", ids.NewId());
        command.Parameters.AddWithValue("@WorkSessionId", movement.WorkSessionId);
        command.Parameters.AddWithValue("@DocumentId", movement.DocumentId);
        command.Parameters.AddWithValue("@BusinessDate", movement.OccurredAt.Date);
        command.Parameters.AddWithValue(
            "@MovementType", isCashIn ? "CashIn" : "CashOut");
        var amount = isCashIn ? movement.Amount : -movement.Amount;
        var money = command.Parameters.Add("@Amount", System.Data.SqlDbType.Decimal);
        money.Precision = 19;
        money.Scale = 4;
        money.Value = amount;
        command.Parameters.AddWithValue(
            "@Reference",
            (object?)movement.Reference ?? movement.DocumentNumber);
        command.Parameters.AddWithValue(
            "@SourceKey", $"cash-movement:{movement.DocumentId:N}");
        command.Parameters.AddWithValue("@OccurredAt", movement.OccurredAt);
        command.Parameters.AddWithValue("@UserId", movement.ConfirmedByUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CompleteDocumentAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        CashMovementDocumentPayload movement,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.CashMovementDocuments
            SET Status=N'Processed',ProcessedAt=SYSDATETIMEOFFSET()
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId
              AND Status=N'Accepted';
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", movement.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", movement.BusinessId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new System.Data.DBConcurrencyException(
                "The cash movement could not be completed.");
    }

    private async Task InsertOutboxAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        CashMovementDocumentPayload movement,
        string payload,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.ServerOutboxMessages
              (MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            VALUES(@MessageId,@DocumentId,@DocumentType,
                   N'work-sessions.cash-movement.processed',@Payload,@Now);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MessageId", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", movement.DocumentId);
        command.Parameters.AddWithValue(
            "@DocumentType",
            CashMovementDocumentTypes.FromDirection(movement.Direction));
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
