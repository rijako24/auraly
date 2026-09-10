using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Application;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

/// <summary>
/// Publishes the derived work owned by the fiscal, accounting and reporting
/// engines, then completes the operational server-outbox record. Replaying a
/// completed document is safe because every destination engine is idempotent.
/// </summary>
public sealed class SqlDocumentProcessingCompletionObserver(
    SqlServerConnectionFactory connections,
    FiscalProcessingCoordinator fiscal,
    AccountingProcessingCoordinator accounting,
    SalesReportingProcessingCoordinator reporting,
    TimeProvider timeProvider) : IDocumentProcessingCompletionObserver
{
    public async Task ObserveAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken)
    {
        try
        {
            if (FiscalGenerationPolicy.Supports(signal.DocumentType))
                await fiscal.RequestGenerationAsync(
                    signal.BusinessId, signal.DocumentId, cancellationToken);

            if (signal.EconomicEffectsEnabled &&
                AccountingProcessingPolicy.Supports(signal.DocumentType))
                await accounting.RequestPostingAsync(
                    signal.BusinessId, signal.DocumentId,
                    signal.DocumentType, cancellationToken);

            if (signal.EconomicEffectsEnabled &&
                SalesReportingProcessingPolicy.Supports(signal.DocumentType))
                await reporting.RequestProjectionAsync(
                    signal.BusinessId, signal.DocumentId,
                    signal.DocumentType, cancellationToken);

            await CompleteOperationalOutboxAsync(signal, cancellationToken);
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(signal, exception, CancellationToken.None);
            throw;
        }
    }

    private async Task CompleteOperationalOutboxAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.ServerOutboxMessages
            SET ProcessedAt=@Now,AttemptCount=AttemptCount+1,LastError=NULL
            WHERE DocumentId=@DocumentId
              AND DocumentType=@DocumentType
              AND ProcessedAt IS NULL
              AND Type<>N'FiscalDocument.DianAccepted'
              AND EXISTS
              (
                SELECT 1 FROM dbo.DocumentProcessingJobs job
                WHERE job.DocumentId=@DocumentId
                  AND job.DocumentType=@DocumentType
                  AND job.BusinessId=@BusinessId
                  AND job.Status=N'Completed'
              );
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        AddScope(command, signal);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(
        DocumentProcessingSignal signal,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            const string sql = """
                UPDATE dbo.ServerOutboxMessages
                SET AttemptCount=AttemptCount+1,LastError=@Error
                WHERE DocumentId=@DocumentId
                  AND DocumentType=@DocumentType
                  AND ProcessedAt IS NULL
                  AND Type<>N'FiscalDocument.DianAccepted'
                  AND EXISTS
                  (
                    SELECT 1 FROM dbo.DocumentProcessingJobs job
                    WHERE job.DocumentId=@DocumentId
                      AND job.DocumentType=@DocumentType
                      AND job.BusinessId=@BusinessId
                  );
                """;
            await using var connection = connections.Create();
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(sql, connection);
            var error = string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message.Trim();
            command.Parameters.AddWithValue(
                "@Error", error.Length <= 2000 ? error : error[..2000]);
            AddScope(command, signal);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // The original publication failure must reach the broker so the
            // durable operational message is retried.
        }
    }

    private static void AddScope(
        SqlCommand command,
        DocumentProcessingSignal signal)
    {
        command.Parameters.AddWithValue("@DocumentId", signal.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", signal.DocumentType);
        command.Parameters.AddWithValue("@BusinessId", signal.BusinessId);
    }
}
