using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

    public static Task InsertSourceAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        Guid tenantId, Guid businessId, Guid documentId, string documentType,
        string payload, DateTimeOffset occurredAt, IAuralyIdGenerator ids,
        TimeProvider timeProvider, CancellationToken cancellationToken,
        AccountingJobRequirement requirement = AccountingJobRequirement.AccountingEntryOnly) =>
        InsertSourceAsync(session.Connection, session.Transaction, tenantId, businessId, documentId,
            documentType, payload, occurredAt, ids, timeProvider, cancellationToken, requirement);

    internal sealed record Source(Guid TenantId, Guid BusinessId, Guid DocumentId, string DocumentType,
        string Payload, DateTimeOffset OccurredAt, Guid JobId);

    public static Task InsertSourceAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid tenantId, Guid businessId, Guid documentId, string documentType,
        string payload, DateTimeOffset occurredAt, IAuralyIdGenerator ids,
        TimeProvider timeProvider, CancellationToken cancellationToken,
        AccountingJobRequirement requirement = AccountingJobRequirement.AccountingEntryOnly,
        Guid? jobId = null) => InsertSourcesAsync(connection, transaction,
            [new(tenantId, businessId, documentId, documentType, payload, occurredAt, jobId ?? ids.NewId())],
            timeProvider.GetUtcNow(), requirement, cancellationToken);

    internal static async Task InsertSourcesAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<Source> sources, DateTimeOffset now, AccountingJobRequirement requirement,
        CancellationToken cancellationToken)
    {
        if (sources.Count is < 1 or > 100 || sources.Select(source => source.DocumentId).Distinct().Count() != sources.Count ||
            sources.Select(source => (source.TenantId, source.BusinessId)).Distinct().Count() != 1)
            throw new ArgumentException("Accounting sources must be a bounded batch from one business.", nameof(sources));
        await using var command = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Sources TABLE(DocumentId uniqueidentifier PRIMARY KEY,DocumentType nvarchar(64),
              Payload nvarchar(max),PayloadHash binary(32),OccurredAt datetimeoffset,JobId uniqueidentifier,
              EntryRequired bit);
            INSERT @Sources SELECT j.DocumentId,j.DocumentType,j.Payload,CONVERT(binary(32),j.Hash,2),
              j.OccurredAt,j.JobId,CONVERT(bit,CASE WHEN EXISTS(
                SELECT 1 FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND Status=N'Ready' AND EffectiveFrom<=CONVERT(date,j.OccurredAt))
                THEN 1 ELSE 0 END)
            FROM OPENJSON(@SourcesJson) WITH(DocumentId uniqueidentifier,DocumentType nvarchar(64),
              Payload nvarchar(max),Hash varchar(64),OccurredAt datetimeoffset,JobId uniqueidentifier) j;
            IF EXISTS(SELECT 1 FROM @Sources i JOIN dbo.AccountingSourceDocuments s
                ON s.SourceDocumentId=i.DocumentId AND s.SourceDocumentType=i.DocumentType
                WHERE s.BusinessId<>@BusinessId OR s.TenantId<>@TenantId OR s.PayloadHash<>i.PayloadHash)
              THROW 51732,N'The accounting source was reused with different data.',1;
            INSERT dbo.AccountingSourceDocuments
              (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
               PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT i.DocumentId,i.DocumentType,@TenantId,@BusinessId,i.Payload,i.PayloadHash,
              i.OccurredAt,@Now,i.EntryRequired FROM @Sources i
            WHERE (i.EntryRequired=1 OR @PreserveCommercialEffects=1)
              AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments s WITH(UPDLOCK,HOLDLOCK)
                WHERE s.SourceDocumentId=i.DocumentId AND s.SourceDocumentType=i.DocumentType);
            INSERT dbo.AccountingPostingJobs
              (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
               SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
            SELECT i.JobId,s.TenantId,s.BusinessId,s.SourceDocumentId,s.SourceDocumentType,
              s.PayloadHash,s.OccurredAt,s.AccountingEntryRequired,N'Pending',0,@Now
            FROM @Sources i JOIN dbo.AccountingSourceDocuments s
              ON s.SourceDocumentId=i.DocumentId AND s.SourceDocumentType=i.DocumentType
            WHERE s.BusinessId=@BusinessId AND s.AccountingEntryRequired IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs j WITH(UPDLOCK,HOLDLOCK)
                WHERE j.SourceDocumentId=s.SourceDocumentId AND j.SourceDocumentType=s.SourceDocumentType);
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", sources[0].TenantId);
        command.Parameters.AddWithValue("@BusinessId", sources[0].BusinessId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@PreserveCommercialEffects", requirement == AccountingJobRequirement.PreserveCommercialEffects);
        command.Parameters.Add("@SourcesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(sources.Select(source => new {
            source.DocumentId, source.DocumentType, source.Payload, source.OccurredAt, source.JobId,
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Payload))) }));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
