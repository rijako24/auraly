using System.Data;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDocumentInitialSignalPublicationStore(
    SqlServerConnectionFactory connections) : IDocumentInitialSignalPublicationStore
{
    private const int MaximumBatch = 100;
    private const int LeaseSeconds = 120;

    public async Task<DocumentInitialSignalClaim> ClaimAsync(
        DocumentProcessingSignal? requested,
        Guid? businessId,
        int maximum,
        CancellationToken cancellationToken)
    {
        var take = requested is null && businessId is null ? Math.Clamp(maximum, 1, MaximumBatch) : 1;
        var lease = Guid.NewGuid();
        var requestedFilter = requested is not null
            ? "AND j.JobId=@JobId AND j.BusinessId=@BusinessId AND j.DocumentId=@DocumentId AND j.DocumentType=@DocumentType"
            : businessId is null ? "" : "AND j.BusinessId=@BusinessId";
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            DECLARE @Now datetimeoffset(7)=SYSUTCDATETIME();
            DECLARE @Claimed TABLE(JobId uniqueidentifier PRIMARY KEY, AvailableAt datetimeoffset(7), CreatedAt datetimeoffset(7));
            ;WITH candidate AS
            (
              SELECT TOP(@Take) j.JobId
              FROM dbo.DocumentProcessingJobs j WITH(UPDLOCK,READPAST,READCOMMITTEDLOCK)
              INNER JOIN dbo.BusinessProcessingCursors c ON c.BusinessId=j.BusinessId
                AND j.ProcessingSequence=c.LastCompletedSequence+1
              WHERE j.Status IN (N'Pending',N'RetryScheduled') AND j.InitialSignalPublishedAt IS NULL
                AND j.AvailableAt<=@Now
                AND (j.InitialSignalLeaseUntil IS NULL OR j.InitialSignalLeaseUntil<=@Now)
                {requestedFilter}
              ORDER BY j.AvailableAt,j.CreatedAt,j.JobId
            )
            UPDATE job SET InitialSignalLeaseId=@Lease,
              InitialSignalLeaseUntil=DATEADD(second,@LeaseSeconds,@Now)
            OUTPUT inserted.JobId,inserted.AvailableAt,inserted.CreatedAt INTO @Claimed
            FROM dbo.DocumentProcessingJobs job
            INNER JOIN candidate c ON c.JobId=job.JobId;

            SELECT j.JobId,j.BusinessId,j.DocumentId,j.DocumentType,
              {SqlDocumentProcessingWorkSource.EconomicEffectsColumn} AS EconomicEffectsEnabled,
              CASE WHEN j.Status=N'RetryScheduled' THEN j.AttemptCount ELSE 0 END
            FROM @Claimed c
            INNER JOIN dbo.DocumentProcessingJobs j ON j.JobId=c.JobId
            INNER JOIN dbo.DocumentProcessingPayloads p ON p.BusinessId=j.BusinessId
              AND p.DocumentId=j.DocumentId AND p.DocumentType=j.DocumentType
            ORDER BY c.AvailableAt,c.CreatedAt,c.JobId;
            """, connection);
        command.Parameters.AddWithValue("@Take", take);
        command.Parameters.AddWithValue("@Lease", lease);
        command.Parameters.AddWithValue("@LeaseSeconds", LeaseSeconds);
        if ((requested?.BusinessId ?? businessId) is { } scope)
            command.Parameters.AddWithValue("@BusinessId", scope);
        if (requested is not null)
        {
            command.Parameters.AddWithValue("@JobId", requested.MovementId);
            command.Parameters.AddWithValue("@DocumentId", requested.DocumentId);
            command.Parameters.Add("@DocumentType", SqlDbType.NVarChar, 64).Value = requested.DocumentType;
        }

        var signals = new List<DocumentInitialActivation>(take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            signals.Add(new(new DocumentProcessingSignal(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetBoolean(4)), reader.GetInt32(5)));
        return new(lease, signals);
    }

    public async Task MarkPublishedAsync(
        Guid leaseId,
        IReadOnlyCollection<Guid> movementIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(movementIds);
        if (movementIds.Count == 0)
            return;
        if (movementIds.Count > MaximumBatch)
            throw new ArgumentOutOfRangeException(nameof(movementIds));

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE job SET InitialSignalPublishedAt=SYSUTCDATETIME(),
              InitialSignalLeaseId=NULL,InitialSignalLeaseUntil=NULL
            FROM dbo.DocumentProcessingJobs job
            INNER JOIN OPENJSON(@Movements) WITH(JobId uniqueidentifier '$') sent ON sent.JobId=job.JobId
            WHERE job.InitialSignalLeaseId=@Lease AND job.InitialSignalPublishedAt IS NULL;
            """, connection);
        command.Parameters.AddWithValue("@Lease", leaseId);
        command.Parameters.Add("@Movements", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(movementIds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
