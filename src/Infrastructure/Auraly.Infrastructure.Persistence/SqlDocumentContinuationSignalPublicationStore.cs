using System.Data;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDocumentContinuationSignalPublicationStore(SqlServerConnectionFactory connections)
    : IDocumentContinuationSignalPublicationStore
{
    private const int MaximumBatch = 100;
    private const int LeaseSeconds = 120;
    private const int MaximumRecoveries = 4;

    public async Task<DocumentContinuationClaim> ClaimAsync(CancellationToken ct)
    {
        var leaseId = Guid.NewGuid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            DECLARE @Now datetimeoffset(7)=SYSUTCDATETIME();
            DECLARE @Claimed TABLE(MessageId uniqueidentifier PRIMARY KEY, DocumentId uniqueidentifier,
                DocumentType nvarchar(64), PublicationCount int);
            ;WITH due AS
            (
                SELECT TOP(@Take) MessageId,DocumentId,DocumentType,RecoveryAvailableAt,
                    RecoveryLeaseId,RecoveryPublicationCount
                FROM dbo.ServerOutboxMessages WITH(UPDLOCK,READPAST,READCOMMITTEDLOCK)
                WHERE ProcessedAt IS NULL AND RecoveryAvailableAt<=@Now
                ORDER BY RecoveryAvailableAt,MessageId
            )
            UPDATE due SET RecoveryAvailableAt=DATEADD(second,@LeaseSeconds,@Now),RecoveryLeaseId=@Lease
            OUTPUT inserted.MessageId,inserted.DocumentId,inserted.DocumentType,
                inserted.RecoveryPublicationCount INTO @Claimed;

            SELECT claimed.MessageId,j.JobId,j.BusinessId,j.DocumentId,j.DocumentType,
                {SqlDocumentProcessingWorkSource.EconomicEffectsColumn},claimed.PublicationCount
            FROM @Claimed claimed
            JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=claimed.DocumentId AND j.DocumentType=claimed.DocumentType
            JOIN dbo.DocumentProcessingPayloads p ON p.BusinessId=j.BusinessId
                AND p.DocumentId=j.DocumentId AND p.DocumentType=j.DocumentType
            WHERE j.Status=N'Completed'
            ORDER BY j.BusinessId,j.ProcessingSequence;
            """, connection);
        command.Parameters.AddWithValue("@Take", MaximumBatch);
        command.Parameters.AddWithValue("@Lease", leaseId);
        command.Parameters.AddWithValue("@LeaseSeconds", LeaseSeconds);

        var activations = new List<DocumentContinuationActivation>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            activations.Add(new(reader.GetGuid(0), new(reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetString(4), reader.GetBoolean(5)), reader.GetInt32(6)));
        return new(leaseId, activations);
    }

    public async Task MarkPublishedAsync(Guid leaseId, IReadOnlyCollection<Guid> outboxMessageIds, CancellationToken ct)
    {
        if (outboxMessageIds.Count == 0)
            return;
        if (outboxMessageIds.Count > MaximumBatch)
            throw new ArgumentOutOfRangeException(nameof(outboxMessageIds));

        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            UPDATE message SET RecoveryLeaseId=NULL,RecoveryPublicationCount=RecoveryPublicationCount+1,
              RecoveryAvailableAt=CASE WHEN RecoveryPublicationCount+1>=@Maximum THEN NULL
                ELSE DATEADD(minute,5*POWER(2,RecoveryPublicationCount),SYSUTCDATETIME()) END
            FROM dbo.ServerOutboxMessages message
            JOIN OPENJSON(@Published) WITH (MessageId uniqueidentifier '$') accepted ON accepted.MessageId=message.MessageId
            WHERE message.RecoveryLeaseId=@Lease AND message.ProcessedAt IS NULL;
            """, connection);
        command.Parameters.AddWithValue("@Lease", leaseId);
        command.Parameters.AddWithValue("@Maximum", MaximumRecoveries);
        command.Parameters.Add("@Published", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(outboxMessageIds);
        await command.ExecuteNonQueryAsync(ct);
    }
}
