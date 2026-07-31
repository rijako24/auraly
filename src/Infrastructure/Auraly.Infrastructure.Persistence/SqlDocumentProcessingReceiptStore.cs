using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDocumentProcessingReceiptStore(
    SqlServerConnectionFactory connections,
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator idGenerator,
    TimeProvider timeProvider)
    : IDocumentProcessingReceiptStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public async Task<ProcessingLeaseResult> TryAcquireAsync(
        DocumentProcessingContext context,
        CancellationToken cancellationToken)
    {
        var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            const string jobSql = """
                SELECT j.JobId, j.BusinessId, j.ProcessingSequence, j.Status,
                       j.AvailableAt, j.LeaseExpiresAt, c.LastCompletedSequence
                FROM dbo.DocumentProcessingJobs j WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.BusinessProcessingCursors c WITH (UPDLOCK, HOLDLOCK)
                    ON c.BusinessId = j.BusinessId
                WHERE j.DocumentId = @DocumentId
                  AND j.DocumentType = @DocumentType;
                """;
            Guid jobId;
            Guid businessId;
            long processingSequence;
            string jobStatus;
            DateTimeOffset availableAt;
            DateTimeOffset? leaseExpiresAt;
            long lastCompletedSequence;
            await using (var job = new SqlCommand(jobSql, connection, transaction))
            {
                AddContext(job, context);
                await using var reader = await job.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        "The confirmed document has no durable processing job.");
                }

                jobId = reader.GetGuid(0);
                businessId = reader.GetGuid(1);
                processingSequence = reader.GetInt64(2);
                jobStatus = reader.GetString(3);
                availableAt = reader.GetDateTimeOffset(4);
                leaseExpiresAt = reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5);
                lastCompletedSequence = reader.GetInt64(6);
            }

            if (businessId != context.BusinessId.Value)
            {
                throw new InvalidOperationException(
                    "The durable processing job belongs to another business.");
            }

            if (string.Equals(jobStatus, "Completed", StringComparison.Ordinal))
            {
                await DisposeWithRollbackAsync(connection, transaction, cancellationToken);
                return ProcessingLeaseResult.AlreadyCompleted;
            }

            var now = timeProvider.GetUtcNow();
            if (processingSequence != lastCompletedSequence + 1 ||
                availableAt > now ||
                (string.Equals(jobStatus, "Processing", StringComparison.Ordinal) &&
                 leaseExpiresAt > now))
            {
                await DisposeWithRollbackAsync(connection, transaction, cancellationToken);
                return ProcessingLeaseResult.Busy;
            }

            const string acquireJobSql = """
                UPDATE dbo.DocumentProcessingJobs
                SET Status = N'Processing',
                    AttemptCount = AttemptCount + 1,
                    LeaseOwner = @LeaseOwner,
                    LeaseExpiresAt = @LeaseExpiresAt,
                    StartedAt = COALESCE(StartedAt, @StartedAt),
                    LastError = NULL
                WHERE JobId = @JobId;
                """;
            await using (var acquireJob = new SqlCommand(acquireJobSql, connection, transaction))
            {
                acquireJob.Parameters.AddWithValue("@JobId", jobId);
                acquireJob.Parameters.AddWithValue(
                    "@LeaseOwner",
                    $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}");
                acquireJob.Parameters.AddWithValue("@LeaseExpiresAt", now.Add(LeaseDuration));
                acquireJob.Parameters.AddWithValue("@StartedAt", now);
                if (await acquireJob.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new DBConcurrencyException(
                        "The durable document job could not be acquired.");
                }
            }

            var receiptId = await UpsertProcessingReceiptAsync(
                connection,
                transaction,
                context,
                now,
                cancellationToken);
            sessions.Set(
                connection,
                transaction,
                context,
                receiptId,
                jobId,
                processingSequence);
            return ProcessingLeaseResult.Acquired;
        }
        catch
        {
            await DisposeWithRollbackAsync(connection, transaction, CancellationToken.None);
            throw;
        }
    }

    public async Task MarkCompletedAsync(
        DocumentProcessingContext context,
        CancellationToken cancellationToken)
    {
        var session = sessions.Take();
        EnsureContext(session.Context, context);
        try
        {
            var completedAt = timeProvider.GetUtcNow();
            const string sql = """
                UPDATE dbo.DocumentProcessingReceipts
                SET Status = N'Completed', CompletedAt = @CompletedAt, LastError = NULL
                WHERE ReceiptId = @ReceiptId;

                UPDATE dbo.DocumentProcessingJobs
                SET Status = N'Completed', CompletedAt = @CompletedAt,
                    LeaseOwner = NULL, LeaseExpiresAt = NULL, LastError = NULL
                WHERE JobId = @JobId AND Status = N'Processing';

                UPDATE dbo.BusinessProcessingCursors
                SET LastCompletedSequence = @ProcessingSequence, UpdatedAt = @CompletedAt
                WHERE BusinessId = @BusinessId
                  AND LastCompletedSequence = @PreviousSequence;
                """;
            await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@ReceiptId", session.ReceiptId);
            command.Parameters.AddWithValue("@JobId", session.JobId);
            command.Parameters.AddWithValue("@BusinessId", context.BusinessId.Value);
            command.Parameters.AddWithValue("@ProcessingSequence", session.ProcessingSequence);
            command.Parameters.AddWithValue("@PreviousSequence", session.ProcessingSequence - 1);
            command.Parameters.AddWithValue("@CompletedAt", completedAt);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 3)
            {
                throw new DBConcurrencyException(
                    "The document job and business cursor were not completed atomically.");
            }

            await session.Transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await session.Transaction.DisposeAsync();
            await session.Connection.DisposeAsync();
        }
    }

    public async Task MarkFailedAsync(
        DocumentProcessingContext context,
        Exception error,
        CancellationToken cancellationToken)
    {
        var session = sessions.Take();
        EnsureContext(session.Context, context);
        await session.Transaction.RollbackAsync(CancellationToken.None);
        await session.Transaction.DisposeAsync();
        await session.Connection.DisposeAsync();

        var now = timeProvider.GetUtcNow();
        var safeError = error.Message.Length <= 2000
            ? error.Message
            : error.Message[..2000];
        const string sql = """
            UPDATE dbo.DocumentProcessingJobs
            SET Status = CASE
                    WHEN AttemptCount + 1 >= 5 THEN N'NeedsIntervention'
                    ELSE N'RetryScheduled'
                END,
                AttemptCount = AttemptCount + 1,
                AvailableAt = CASE WHEN AttemptCount + 1 >= 5 THEN @FailedAt ELSE @AvailableAt END,
                LeaseOwner = NULL,
                LeaseExpiresAt = NULL,
                LastError = @LastError
            WHERE JobId = @JobId AND Status <> N'Completed';

            UPDATE dbo.DocumentProcessingReceipts
            SET Status = N'Failed', AttemptCount = AttemptCount + 1,
                AcquiredAt = @FailedAt, CompletedAt = NULL, LastError = @LastError
            WHERE DocumentId = @DocumentId AND DocumentType = @DocumentType;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.DocumentProcessingReceipts
                    (ReceiptId, DocumentId, DocumentType, Status, AttemptCount, AcquiredAt, LastError)
                VALUES
                    (@ReceiptId, @DocumentId, @DocumentType, N'Failed', 1, @FailedAt, @LastError);
            END;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        AddContext(command, context);
        command.Parameters.AddWithValue("@JobId", session.JobId);
        command.Parameters.AddWithValue("@ReceiptId", idGenerator.NewId());
        command.Parameters.AddWithValue("@FailedAt", now);
        command.Parameters.AddWithValue("@AvailableAt", now.Add(RetryDelay));
        command.Parameters.AddWithValue("@LastError", safeError);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Guid> UpsertProcessingReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DocumentProcessingContext context,
        DateTimeOffset acquiredAt,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT ReceiptId
            FROM dbo.DocumentProcessingReceipts WITH (UPDLOCK, HOLDLOCK)
            WHERE DocumentId = @DocumentId AND DocumentType = @DocumentType;
            """;
        Guid? receiptId = null;
        await using (var select = new SqlCommand(selectSql, connection, transaction))
        {
            AddContext(select, context);
            var value = await select.ExecuteScalarAsync(cancellationToken);
            if (value is Guid existing) receiptId = existing;
        }

        if (receiptId is null)
        {
            receiptId = idGenerator.NewId();
            const string insertSql = """
                INSERT INTO dbo.DocumentProcessingReceipts
                    (ReceiptId, DocumentId, DocumentType, Status, AttemptCount, AcquiredAt)
                VALUES
                    (@ReceiptId, @DocumentId, @DocumentType, N'Processing', 1, @AcquiredAt);
                """;
            await using var insert = new SqlCommand(insertSql, connection, transaction);
            AddContext(insert, context);
            insert.Parameters.AddWithValue("@ReceiptId", receiptId.Value);
            insert.Parameters.AddWithValue("@AcquiredAt", acquiredAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string updateSql = """
                UPDATE dbo.DocumentProcessingReceipts
                SET Status = N'Processing', AttemptCount = AttemptCount + 1,
                    AcquiredAt = @AcquiredAt, CompletedAt = NULL, LastError = NULL
                WHERE ReceiptId = @ReceiptId;
                """;
            await using var update = new SqlCommand(updateSql, connection, transaction);
            update.Parameters.AddWithValue("@ReceiptId", receiptId.Value);
            update.Parameters.AddWithValue("@AcquiredAt", acquiredAt);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return receiptId.Value;
    }

    private static void AddContext(SqlCommand command, DocumentProcessingContext context)
    {
        command.Parameters.AddWithValue("@DocumentId", context.DocumentId.Value);
        command.Parameters.AddWithValue("@DocumentType", context.DocumentType);
    }

    private static async Task DisposeWithRollbackAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static void EnsureContext(
        DocumentProcessingContext actual,
        DocumentProcessingContext expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                "The active SQL processing session belongs to another document.");
        }
    }
}
