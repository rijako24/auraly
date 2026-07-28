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
            const string selectSql = """
                SELECT ReceiptId, Status, AcquiredAt
                FROM dbo.DocumentProcessingReceipts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId = @TenantId
                  AND DocumentId = @DocumentId
                  AND DocumentType = @DocumentType;
                """;
            Guid receiptId;
            string? status = null;
            DateTimeOffset? acquiredAt = null;
            await using (var select = new SqlCommand(selectSql, connection, transaction))
            {
                AddContext(select, context);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    receiptId = reader.GetGuid(0);
                    status = reader.GetString(1);
                    acquiredAt = reader.GetDateTimeOffset(2);
                }
                else
                {
                    receiptId = idGenerator.NewId();
                }
            }

            if (string.Equals(status, "Completed", StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                await connection.DisposeAsync();
                return ProcessingLeaseResult.AlreadyCompleted;
            }

            var now = timeProvider.GetUtcNow();
            if (string.Equals(status, "Processing", StringComparison.Ordinal) &&
                acquiredAt > now.AddMinutes(-2))
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                await connection.DisposeAsync();
                return ProcessingLeaseResult.Busy;
            }

            if (status is null)
            {
                const string insertSql = """
                    INSERT INTO dbo.DocumentProcessingReceipts
                    (
                        ReceiptId, TenantId, DocumentId, DocumentType,
                        Status, AttemptCount, AcquiredAt
                    )
                    VALUES
                    (
                        @ReceiptId, @TenantId, @DocumentId, @DocumentType,
                        'Processing', 1, @AcquiredAt
                    );
                    """;
                await using var insert = new SqlCommand(insertSql, connection, transaction);
                AddContext(insert, context);
                insert.Parameters.AddWithValue("@ReceiptId", receiptId);
                insert.Parameters.AddWithValue("@AcquiredAt", now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                const string updateSql = """
                    UPDATE dbo.DocumentProcessingReceipts
                    SET Status = 'Processing',
                        AttemptCount = AttemptCount + 1,
                        AcquiredAt = @AcquiredAt,
                        CompletedAt = NULL,
                        LastError = NULL
                    WHERE ReceiptId = @ReceiptId;
                    """;
                await using var update = new SqlCommand(updateSql, connection, transaction);
                update.Parameters.AddWithValue("@ReceiptId", receiptId);
                update.Parameters.AddWithValue("@AcquiredAt", now);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            sessions.Set(connection, transaction, context, receiptId);
            return ProcessingLeaseResult.Acquired;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
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
            const string sql = """
                UPDATE dbo.DocumentProcessingReceipts
                SET Status = 'Completed',
                    CompletedAt = @CompletedAt,
                    LastError = NULL
                WHERE ReceiptId = @ReceiptId;
                """;
            await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@ReceiptId", session.ReceiptId);
            command.Parameters.AddWithValue("@CompletedAt", timeProvider.GetUtcNow());
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new DBConcurrencyException("The document receipt could not be completed.");
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
        string error,
        CancellationToken cancellationToken)
    {
        var session = sessions.Take();
        EnsureContext(session.Context, context);
        await session.Transaction.RollbackAsync(CancellationToken.None);
        await session.Transaction.DisposeAsync();
        await session.Connection.DisposeAsync();

        const string sql = """
            UPDATE dbo.DocumentProcessingReceipts
            SET Status = 'Failed',
                LastError = @LastError,
                CompletedAt = NULL
            WHERE TenantId = @TenantId
              AND DocumentId = @DocumentId
              AND DocumentType = @DocumentType;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.DocumentProcessingReceipts
                (
                    ReceiptId, TenantId, DocumentId, DocumentType,
                    Status, AttemptCount, AcquiredAt, LastError
                )
                VALUES
                (
                    @ReceiptId, @TenantId, @DocumentId, @DocumentType,
                    'Failed', 1, @AcquiredAt, @LastError
                );
            END;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddContext(command, context);
        command.Parameters.AddWithValue("@ReceiptId", idGenerator.NewId());
        command.Parameters.AddWithValue("@AcquiredAt", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue(
            "@LastError",
            error.Length <= 2000 ? error : error[..2000]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddContext(SqlCommand command, DocumentProcessingContext context)
    {
        command.Parameters.AddWithValue("@TenantId", context.TenantId.Value);
        command.Parameters.AddWithValue("@DocumentId", context.DocumentId.Value);
        command.Parameters.AddWithValue("@DocumentType", context.DocumentType);
    }

    private static void EnsureContext(
        DocumentProcessingContext actual,
        DocumentProcessingContext expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException("The active SQL processing session belongs to another document.");
        }
    }
}

