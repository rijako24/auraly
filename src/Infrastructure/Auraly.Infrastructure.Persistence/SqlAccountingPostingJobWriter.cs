using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Auraly.Infrastructure.Persistence;

internal enum AccountingJobRequirement
{
    AccountingEntryOnly,
    PreserveCommercialEffects
}

internal static class SqlAccountingPostingJobWriter
{
    public static async Task InsertAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        ConfirmedDocument document,
        DateTimeOffset occurredAt,
        IAuralyIdGenerator ids,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        AccountingJobRequirement requirement = AccountingJobRequirement.AccountingEntryOnly)
    {
        const string sql = """
            DECLARE @AccountingEntryRequired bit=CONVERT(bit,CASE WHEN EXISTS
            (
              SELECT 1 FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
              WHERE TenantId=@TenantId AND Status=N'Ready'
                AND EffectiveFrom<=CONVERT(date,@OccurredAt)
            ) THEN 1 ELSE 0 END);

            INSERT dbo.AccountingSourceDocuments
            (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
             PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT p.DocumentId,p.DocumentType,@TenantId,@BusinessId,p.PayloadJson,
                   p.PayloadHash,@OccurredAt,@CreatedAt,@AccountingEntryRequired
            FROM dbo.DocumentProcessingPayloads p
            WHERE p.DocumentId=@DocumentId AND p.DocumentType=@DocumentType
              AND p.BusinessId=@BusinessId
              AND (@AccountingEntryRequired=1 OR @PreserveCommercialEffects=1)
              AND NOT EXISTS
              (
                SELECT 1 FROM dbo.AccountingSourceDocuments s WITH(UPDLOCK,HOLDLOCK)
                WHERE s.SourceDocumentId=p.DocumentId
                  AND s.SourceDocumentType=p.DocumentType
              );

            INSERT dbo.AccountingPostingJobs
            (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
             SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,
             Status,AttemptCount,CreatedAt)
            SELECT @JobId,s.TenantId,s.BusinessId,s.SourceDocumentId,
                   s.SourceDocumentType,s.PayloadHash,s.OccurredAt,
                   s.AccountingEntryRequired,N'Pending',0,@CreatedAt
            FROM dbo.AccountingSourceDocuments s
            WHERE s.SourceDocumentId=@DocumentId
              AND s.SourceDocumentType=@DocumentType
              AND s.BusinessId=@BusinessId
              AND s.AccountingEntryRequired IS NOT NULL
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
        command.Parameters.AddWithValue(
            "@PreserveCommercialEffects",
            requirement == AccountingJobRequirement.PreserveCommercialEffects);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted is < 0 or > 2)
            throw new InvalidOperationException("An invalid number of accounting jobs was created.");
    }

    public static async Task InsertSourceAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        Guid tenantId, Guid businessId, Guid documentId, string documentType,
        string payload, DateTimeOffset occurredAt, IAuralyIdGenerator ids,
        TimeProvider timeProvider, CancellationToken cancellationToken,
        AccountingJobRequirement requirement = AccountingJobRequirement.AccountingEntryOnly)
    {
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        const string sql = """
            DECLARE @AccountingEntryRequired bit=CONVERT(bit,CASE WHEN EXISTS
            (
              SELECT 1 FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
              WHERE TenantId=@TenantId AND Status=N'Ready'
                AND EffectiveFrom<=CONVERT(date,@OccurredAt)
            ) THEN 1 ELSE 0 END);

            INSERT dbo.AccountingSourceDocuments
              (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
               PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT @DocumentId,@DocumentType,@TenantId,@BusinessId,@Payload,@PayloadHash,
                   @OccurredAt,@CreatedAt,@AccountingEntryRequired
            WHERE (@AccountingEntryRequired=1 OR @PreserveCommercialEffects=1)
              AND NOT EXISTS (SELECT 1 FROM dbo.AccountingSourceDocuments s WITH(UPDLOCK,HOLDLOCK)
                WHERE s.SourceDocumentId=@DocumentId AND s.SourceDocumentType=@DocumentType);

            INSERT dbo.AccountingPostingJobs
              (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
               SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
            SELECT @JobId,s.TenantId,s.BusinessId,s.SourceDocumentId,s.SourceDocumentType,
                   s.PayloadHash,s.OccurredAt,s.AccountingEntryRequired,N'Pending',0,@CreatedAt
            FROM dbo.AccountingSourceDocuments s
            WHERE s.SourceDocumentId=@DocumentId AND s.SourceDocumentType=@DocumentType
              AND s.AccountingEntryRequired IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.AccountingPostingJobs j WITH(UPDLOCK,HOLDLOCK)
                WHERE j.SourceDocumentId=@DocumentId AND j.SourceDocumentType=@DocumentType);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@JobId", ids.NewId());
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        command.Parameters.AddWithValue("@OccurredAt", occurredAt);
        command.Parameters.AddWithValue("@CreatedAt", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue(
            "@PreserveCommercialEffects",
            requirement == AccountingJobRequirement.PreserveCommercialEffects);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted is < 0 or > 2)
            throw new InvalidOperationException("An invalid number of accounting jobs was created.");
    }
}
