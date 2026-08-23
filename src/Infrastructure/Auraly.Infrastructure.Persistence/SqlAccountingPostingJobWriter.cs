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
            INSERT dbo.AccountingSourceDocuments
            (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
             PayloadHash,OccurredAt,AcceptedAt)
            SELECT p.DocumentId,p.DocumentType,@TenantId,@BusinessId,p.PayloadJson,
                   p.PayloadHash,@OccurredAt,@CreatedAt
            FROM dbo.DocumentProcessingPayloads p
            INNER JOIN dbo.AccountingTenantSettings settings
              ON settings.TenantId=@TenantId AND settings.Status=N'Ready'
             AND settings.EffectiveFrom<=CONVERT(date,@OccurredAt)
            WHERE p.DocumentId=@DocumentId AND p.DocumentType=@DocumentType
              AND p.BusinessId=@BusinessId
              AND NOT EXISTS
              (
                SELECT 1 FROM dbo.AccountingSourceDocuments s WITH(UPDLOCK,HOLDLOCK)
                WHERE s.SourceDocumentId=p.DocumentId
                  AND s.SourceDocumentType=p.DocumentType
              );

            INSERT dbo.AccountingPostingJobs
            (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
             SourceDocumentType,SourcePayloadHash,OccurredAt,Status,AttemptCount,CreatedAt)
            SELECT @JobId,s.TenantId,s.BusinessId,s.SourceDocumentId,
                   s.SourceDocumentType,s.PayloadHash,s.OccurredAt,N'Pending',0,@CreatedAt
            FROM dbo.AccountingSourceDocuments s
            WHERE s.SourceDocumentId=@DocumentId
              AND s.SourceDocumentType=@DocumentType
              AND s.BusinessId=@BusinessId
              AND NOT EXISTS
              (
                SELECT 1 FROM dbo.AccountingPostingJobs a WITH(UPDLOCK,HOLDLOCK)
                WHERE a.SourceDocumentId=s.SourceDocumentId
                  AND a.SourceDocumentType=s.SourceDocumentType
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
        if (inserted is < 0 or > 2)
            throw new InvalidOperationException("An invalid number of accounting jobs was created.");
    }
}
