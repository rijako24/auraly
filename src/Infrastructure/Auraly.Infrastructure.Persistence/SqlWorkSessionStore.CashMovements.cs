using Auraly.Application.WorkSessions;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.Contracts.WorkSessions;
using Auraly.Domain.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlWorkSessionStore
{
    public async Task<CashMovementAcceptance> AcceptCashMovementAsync(
        WorkSessionIdentity identity, string idempotencyKey, CashMovement movement,
        CancellationToken cancellationToken)
    {
        var requestHash = HashCashMovementRequest(movement);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await FindCashMovementReplayAsync(
                connection, transaction, movement.BusinessId, movement.DocumentId,
                idempotencyKey, requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            await ValidateCashMovementContextAsync(
                connection, transaction, identity, movement, cancellationToken);
            var documentType = CashMovementDocumentTypes.FromDirection(
                movement.Reason.Direction.ToString());
            var number = await AllocateCashMovementNumberAsync(
                connection, transaction, movement.BusinessId, documentType,
                cancellationToken);
            var now = timeProvider.GetUtcNow();
            var sequence = await AllocateCashProcessingSequenceAsync(
                connection, transaction, movement.BusinessId, now, cancellationToken);
            var movementId = ids.NewId();
            var payload = new CashMovementDocumentPayload(
                identity.TenantId, movement.BusinessId, movement.DocumentId,
                movement.WorkSessionId, movement.Reason.ReasonId,
                movement.Reason.Code, movement.Reason.Name,
                movement.Reason.Direction.ToString(),
                movement.Reason.CounterpartAccountingCategory,
                movement.CostCenterId, identity.UserId, number.FullNumber,
                number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive,
                movement.Amount, movement.OccurredAt, movement.Reference, movement.Notes);
            var payloadJson = CashMovementContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
            await InsertCashMovementAcceptanceAsync(
                connection, transaction, identity, movement, documentType, number,
                idempotencyKey, requestHash, payloadHash, now, cancellationToken);
            await InsertCashMovementProcessingJobAsync(
                connection, transaction, movement, documentType, movementId,
                sequence, payloadJson, payloadHash, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CashMovementAcceptance(
                movement.DocumentId, movementId, documentType, number.FullNumber,
                "Accepted", sequence, false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task ValidateCashMovementContextAsync(
        SqlConnection connection, SqlTransaction transaction,
        WorkSessionIdentity identity, CashMovement movement,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(*)
            FROM dbo.WorkSessions ws WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b ON b.BusinessId=ws.BusinessId
            INNER JOIN dbo.BusinessReasons r
              ON r.BusinessId=ws.BusinessId AND r.ReasonId=@ReasonId
            WHERE ws.WorkSessionId=@WorkSessionId AND ws.BusinessId=@BusinessId
              AND ws.UserId=@UserId AND ws.Status=N'Open'
              AND b.TenantId=@TenantId AND r.IsActive=1
              AND r.Code=@ReasonCode AND r.Direction=@Direction;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ReasonId", movement.Reason.ReasonId);
        command.Parameters.AddWithValue("@WorkSessionId", movement.WorkSessionId);
        command.Parameters.AddWithValue("@BusinessId", movement.BusinessId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@ReasonCode", movement.Reason.Code);
        command.Parameters.AddWithValue("@Direction", movement.Reason.Direction.ToString());
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new WorkSessionConflictException(
                "The cash movement session or reason is no longer available.");
        if (movement.CostCenterId is { } costCenterId)
            await ValidateCostCenterAsync(
                connection, transaction, movement.BusinessId, costCenterId,
                cancellationToken);
    }

    private async Task<AuralyDocumentNumberAssignment> AllocateCashMovementNumberAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        string documentType, CancellationToken cancellationToken)
    {
        var prefix = documentType == CashMovementDocumentTypes.Receipt ? "ING" : "EGR";
        Guid seriesId;
        await using (var select = new SqlCommand("""
            SELECT TOP(1) DocumentSeriesId
            FROM dbo.DocumentSeries WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND DocumentType=@DocumentType
              AND DeviceId IS NULL AND IsActive=1
            ORDER BY DocumentSeriesId;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("@BusinessId", businessId);
            select.Parameters.AddWithValue("@DocumentType", documentType);
            seriesId = await select.ExecuteScalarAsync(cancellationToken) is Guid value
                ? value : Guid.Empty;
        }
        if (seriesId == Guid.Empty)
        {
            seriesId = ids.NewId();
            await using var insert = new SqlCommand("""
                INSERT dbo.DocumentSeries
                  (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,
                   SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,
                   IsActive,CreatedAt)
                VALUES(@SeriesId,@BusinessId,NULL,@DocumentType,@Prefix,N'00',
                       8,1,99999999,0,1,@Now);
                INSERT dbo.DocumentSeriesCursors
                  (DocumentSeriesId,NextConsecutive,UpdatedAt)
                VALUES(@SeriesId,1,@Now);
                """, connection, transaction);
            insert.Parameters.AddWithValue("@SeriesId", seriesId);
            insert.Parameters.AddWithValue("@BusinessId", businessId);
            insert.Parameters.AddWithValue("@DocumentType", documentType);
            insert.Parameters.AddWithValue("@Prefix", prefix);
            insert.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        long consecutive;
        await using (var cursor = new SqlCommand("""
            UPDATE dbo.DocumentSeriesCursors WITH (UPDLOCK,HOLDLOCK)
            SET NextConsecutive=NextConsecutive+1,UpdatedAt=@Now
            OUTPUT deleted.NextConsecutive
            WHERE DocumentSeriesId=@SeriesId;
            """, connection, transaction))
        {
            cursor.Parameters.AddWithValue("@SeriesId", seriesId);
            cursor.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            consecutive = Convert.ToInt64(
                await cursor.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException(
                    "The cash movement series cursor is missing."));
        }
        return AuralyDocumentNumberAssignment.Create(
            seriesId, documentType, prefix, "00", consecutive, 8);
    }

    private static async Task<long> AllocateCashProcessingSequenceAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK)
                          WHERE BusinessId=@BusinessId)
              INSERT dbo.BusinessProcessingCursors
                (BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt)
              VALUES(@BusinessId,0,0,@Now);
            UPDATE dbo.BusinessProcessingCursors WITH (UPDLOCK,HOLDLOCK)
            SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now
            OUTPUT inserted.LastAssignedSequence WHERE BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<CashMovementAcceptance?> FindCashMovementReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid documentId, string idempotencyKey, byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT d.DocumentId,j.JobId,d.DocumentType,d.DocumentNumber,d.Status,
                   j.ProcessingSequence,d.RequestHash
            FROM dbo.CashMovementDocuments d WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=d.DocumentId AND j.DocumentType=d.DocumentType
            WHERE d.BusinessId=@BusinessId
              AND (d.DocumentId=@DocumentId OR d.IdempotencyKey=@IdempotencyKey);
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(6).AsSpan().SequenceEqual(requestHash))
            throw new WorkSessionConflictException(
                "The cash movement idempotency key was reused with another payload.");
        return new CashMovementAcceptance(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetInt64(5), true);
    }

    private static async Task InsertCashMovementAcceptanceAsync(
        SqlConnection connection, SqlTransaction transaction,
        WorkSessionIdentity identity, CashMovement movement, string documentType,
        AuralyDocumentNumberAssignment number, string idempotencyKey,
        byte[] requestHash, byte[] payloadHash, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.CashMovementDocuments
              (DocumentId,BusinessId,WorkSessionId,ReasonId,DocumentSeriesId,
               DocumentType,DocumentNumber,DocumentPrefix,DocumentSeriesCode,
               DocumentConsecutive,Direction,Amount,OccurredAt,Reference,Notes,
               CostCenterId,IdempotencyKey,RequestHash,PayloadHash,Status,
               ConfirmedByUserId,AcceptedAt)
            VALUES(@DocumentId,@BusinessId,@WorkSessionId,@ReasonId,@SeriesId,
                   @DocumentType,@DocumentNumber,@Prefix,@SeriesCode,@Consecutive,
                   @Direction,@Amount,@OccurredAt,@Reference,@Notes,@CostCenterId,
                   @IdempotencyKey,@RequestHash,@PayloadHash,N'Accepted',@UserId,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", movement.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", movement.BusinessId);
        command.Parameters.AddWithValue("@WorkSessionId", movement.WorkSessionId);
        command.Parameters.AddWithValue("@ReasonId", movement.Reason.ReasonId);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@DocumentNumber", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@Direction", movement.Reason.Direction.ToString());
        AddMoney(command, "@Amount", movement.Amount);
        command.Parameters.AddWithValue("@OccurredAt", movement.OccurredAt);
        command.Parameters.AddWithValue("@Reference",
            (object?)movement.Reference ?? DBNull.Value);
        command.Parameters.AddWithValue("@Notes", (object?)movement.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@CostCenterId",
            (object?)movement.CostCenterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        command.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCashMovementProcessingJobAsync(
        SqlConnection connection, SqlTransaction transaction, CashMovement movement,
        string documentType, Guid movementId, long sequence, string payload,
        byte[] payloadHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,
               Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,@DocumentType,
                   N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,
               PayloadHash,AcceptedAt)
            VALUES(@DocumentId,@DocumentType,@BusinessId,1,@Payload,@PayloadHash,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@BusinessId", movement.BusinessId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@DocumentId", movement.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] HashCashMovementRequest(CashMovement movement) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            movement.DocumentId,
            movement.BusinessId,
            movement.WorkSessionId,
            movement.Reason.ReasonId,
            movement.Amount,
            movement.OccurredAt,
            movement.Reference,
            movement.Notes,
            movement.CostCenterId
        }));
}
