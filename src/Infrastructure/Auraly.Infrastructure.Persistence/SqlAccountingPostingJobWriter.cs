using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlAccountingPostingJobWriter
{
    public static async Task InsertAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        ConfirmedDocument document,
        DateTimeOffset occurredAt,
        IAuralyIdGenerator ids,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.AccountingPostingJobs
            (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
             SourceDocumentType,SourcePayloadHash,OccurredAt,Status,AttemptCount,CreatedAt)
            SELECT @JobId,@TenantId,@BusinessId,p.DocumentId,p.DocumentType,
                   p.PayloadHash,@OccurredAt,N'Pending',0,@CreatedAt
            FROM dbo.DocumentProcessingPayloads p
            INNER JOIN dbo.AccountingTenantSettings settings
              ON settings.TenantId=@TenantId AND settings.Status=N'Ready'
             AND settings.EffectiveFrom<=CONVERT(date,@OccurredAt)
            WHERE p.DocumentId=@DocumentId AND p.DocumentType=@DocumentType
              AND p.BusinessId=@BusinessId
              AND NOT EXISTS
              (
                SELECT 1 FROM dbo.AccountingPostingJobs a WITH(UPDLOCK,HOLDLOCK)
                WHERE a.SourceDocumentId=p.DocumentId
                  AND a.SourceDocumentType=p.DocumentType
              );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@JobId", ids.NewId());
        command.Parameters.AddWithValue("@TenantId", document.TenantId.Value);
        command.Parameters.AddWithValue("@BusinessId", document.BusinessId.Value);
        command.Parameters.AddWithValue("@DocumentId", document.DocumentId.Value);
        command.Parameters.AddWithValue("@DocumentType", document.DocumentType);
        command.Parameters.AddWithValue("@OccurredAt", occurredAt);
        command.Parameters.AddWithValue("@CreatedAt", timeProvider.GetUtcNow());
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted is not (0 or 1))
            throw new InvalidOperationException("An invalid number of accounting jobs was created.");
    }
}
