using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDocumentProcessingJobStore(
    SqlServerConnectionFactory connections,
    SqlDocumentProcessingSessionAccessor sessions,
    TimeProvider timeProvider)
    : IDocumentProcessingJobStore
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
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sql = """
                SELECT j.JobId,j.BusinessId,j.ProcessingSequence,j.Status,
                       j.AvailableAt,j.LeaseExpiresAt,c.LastCompletedSequence
                FROM dbo.DocumentProcessingJobs j WITH (UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.BusinessProcessingCursors c WITH (UPDLOCK,HOLDLOCK)
                  ON c.BusinessId=j.BusinessId
                WHERE j.DocumentId=@DocumentId AND j.DocumentType=@DocumentType;
                """;
            Guid jobId;
            Guid businessId;
            long sequence;
            string status;
            DateTimeOffset availableAt;
            DateTimeOffset? leaseExpiresAt;
            long lastCompleted;
            await using (var command = new SqlCommand(sql, connection, transaction))
            {
                AddContext(command, context);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "The confirmed document has no durable processing job.");
                jobId = reader.GetGuid(0);
                businessId = reader.GetGuid(1);
                sequence = reader.GetInt64(2);
                status = reader.GetString(3);
                availableAt = reader.GetDateTimeOffset(4);
                leaseExpiresAt = reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5);
                lastCompleted = reader.GetInt64(6);
            }

            if (businessId != context.BusinessId.Value)
                throw new InvalidOperationException(
                    "The durable processing job belongs to another business.");
            if (status == "Completed")
            {
                await DisposeWithRollbackAsync(connection, transaction, cancellationToken);
                return ProcessingLeaseResult.AlreadyCompleted;
            }

            var now = timeProvider.GetUtcNow();
            if (sequence != lastCompleted + 1 || availableAt > now ||
                status == "NeedsIntervention" ||
                status == "Processing" && leaseExpiresAt > now)
            {
                await DisposeWithRollbackAsync(connection, transaction, cancellationToken);
                return ProcessingLeaseResult.Busy;
            }

            await using var acquire = new SqlCommand("""
                UPDATE dbo.DocumentProcessingJobs
                SET Status=N'Processing',AttemptCount=AttemptCount+1,
                    LeaseOwner=@LeaseOwner,LeaseExpiresAt=@LeaseExpiresAt,
                    StartedAt=COALESCE(StartedAt,@StartedAt),LastError=NULL
                WHERE JobId=@JobId;
                """, connection, transaction);
            acquire.Parameters.AddWithValue("@JobId", jobId);
            acquire.Parameters.AddWithValue(
                "@LeaseOwner",
                $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}");
            acquire.Parameters.AddWithValue("@LeaseExpiresAt", now.Add(LeaseDuration));
            acquire.Parameters.AddWithValue("@StartedAt", now);
            if (await acquire.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new DBConcurrencyException(
                    "The durable document job could not be acquired.");

            sessions.Set(connection, transaction, context, jobId, sequence);
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
            await using var command = new SqlCommand("""
                UPDATE dbo.DocumentProcessingJobs
                SET Status=N'Completed',CompletedAt=@CompletedAt,
                    LeaseOwner=NULL,LeaseExpiresAt=NULL,LastError=NULL
                WHERE JobId=@JobId AND Status=N'Processing';

                UPDATE dbo.BusinessProcessingCursors
                SET LastCompletedSequence=@ProcessingSequence,UpdatedAt=@CompletedAt
                WHERE BusinessId=@BusinessId
                  AND LastCompletedSequence=@PreviousSequence;
                """, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@JobId", session.JobId);
            command.Parameters.AddWithValue("@BusinessId", context.BusinessId.Value);
            command.Parameters.AddWithValue("@ProcessingSequence", session.ProcessingSequence);
            command.Parameters.AddWithValue("@PreviousSequence", session.ProcessingSequence - 1);
            command.Parameters.AddWithValue("@CompletedAt", completedAt);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 2)
                throw new DBConcurrencyException(
                    "The document job and business cursor were not completed atomically.");
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

        var safeError = error.Message.Length <= 2000 ? error.Message : error.Message[..2000];
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            long processingSequence;
            int persistedAttempts;
            string status;
            await using (var read = new SqlCommand("""
                SELECT j.ProcessingSequence,j.AttemptCount,j.Status
                FROM dbo.DocumentProcessingJobs j WITH (UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.BusinessProcessingCursors c WITH (UPDLOCK,HOLDLOCK)
                  ON c.BusinessId=j.BusinessId
                WHERE j.JobId=@JobId AND j.BusinessId=@BusinessId;
                """, connection, transaction))
            {
                read.Parameters.AddWithValue("@JobId", session.JobId);
                read.Parameters.AddWithValue("@BusinessId", context.BusinessId.Value);
                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "The failed document job no longer exists.");
                processingSequence = reader.GetInt64(0);
                persistedAttempts = reader.GetInt32(1);
                status = reader.GetString(2);
            }

            if (status is "Completed" or "DeadLettered")
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var now = timeProvider.GetUtcNow();
            var failedAttempts = checked(persistedAttempts + 1);
            var deadLettered = failedAttempts >= 5;
            await using (var update = new SqlCommand("""
                UPDATE dbo.DocumentProcessingJobs
                SET Status=@Status,AttemptCount=@AttemptCount,
                    AvailableAt=@AvailableAt,CompletedAt=@CompletedAt,
                    LeaseOwner=NULL,LeaseExpiresAt=NULL,LastError=@LastError
                WHERE JobId=@JobId AND Status<>N'Completed';
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("@JobId", session.JobId);
                update.Parameters.AddWithValue(
                    "@Status", deadLettered ? "DeadLettered" : "RetryScheduled");
                update.Parameters.AddWithValue("@AttemptCount", failedAttempts);
                update.Parameters.AddWithValue(
                    "@AvailableAt", deadLettered ? now : now.Add(RetryDelay));
                update.Parameters.AddWithValue(
                    "@CompletedAt", deadLettered ? now : DBNull.Value);
                update.Parameters.AddWithValue("@LastError", safeError);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException(
                        "The failed document job could not be persisted.");
            }

            if (deadLettered)
            {
                await using var advance = new SqlCommand("""
                    UPDATE dbo.BusinessProcessingCursors
                    SET LastCompletedSequence=@ProcessingSequence,UpdatedAt=@CompletedAt
                    WHERE BusinessId=@BusinessId
                      AND LastCompletedSequence=@PreviousSequence;
                    """, connection, transaction);
                advance.Parameters.AddWithValue("@BusinessId", context.BusinessId.Value);
                advance.Parameters.AddWithValue("@ProcessingSequence", processingSequence);
                advance.Parameters.AddWithValue("@PreviousSequence", processingSequence - 1);
                advance.Parameters.AddWithValue("@CompletedAt", now);
                if (await advance.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException(
                        "The dead-lettered movement could not release its ordered position.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
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
            throw new InvalidOperationException(
                "The active SQL processing session belongs to another document.");
    }
}
