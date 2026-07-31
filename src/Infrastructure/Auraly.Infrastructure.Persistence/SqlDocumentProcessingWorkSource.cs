using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDocumentProcessingWorkSource(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider)
    : IDocumentProcessingWorkSource
{
    private static readonly TimeSpan RecoveryAge = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<ConfirmedDocument>> LoadReadyAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (@MaximumCount)
                   b.TenantId, j.BusinessId, j.DocumentId, j.DocumentType,
                   p.PayloadJson, p.AcceptedAt
            FROM dbo.DocumentProcessingJobs j
            INNER JOIN dbo.BusinessProcessingCursors c
                ON c.BusinessId = j.BusinessId
            INNER JOIN dbo.DocumentProcessingPayloads p
                ON p.DocumentId = j.DocumentId AND p.DocumentType = j.DocumentType
            INNER JOIN dbo.Businesses b
                ON b.BusinessId = j.BusinessId
            WHERE j.ProcessingSequence = c.LastCompletedSequence + 1
              AND j.Status IN (N'Pending', N'RetryScheduled', N'Processing')
              AND j.AvailableAt <= @Now
              AND j.CreatedAt <= @RecoveryCutoff
              AND (j.Status <> N'Processing' OR j.LeaseExpiresAt <= @Now)
            ORDER BY j.CreatedAt, j.BusinessId;
            """;
        var now = timeProvider.GetUtcNow();
        var result = new List<ConfirmedDocument>();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@MaximumCount", maximumCount);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@RecoveryCutoff", now.Subtract(RecoveryAge));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ConfirmedDocument(
                new TenantId(reader.GetGuid(0)),
                new BusinessId(reader.GetGuid(1)),
                new DocumentId(reader.GetGuid(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDateTimeOffset(5)));
        }

        return result;
    }
}

