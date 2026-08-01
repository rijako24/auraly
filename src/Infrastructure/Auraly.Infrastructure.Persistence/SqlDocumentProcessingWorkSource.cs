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
    public async Task<DocumentProcessingWork> LoadAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT j.Status,j.ProcessingSequence,c.LastCompletedSequence,
                   j.AvailableAt,j.LeaseExpiresAt,
                   b.TenantId,j.BusinessId,j.DocumentId,j.DocumentType,
                   p.PayloadJson,p.AcceptedAt
            FROM dbo.DocumentProcessingJobs j
            INNER JOIN dbo.BusinessProcessingCursors c ON c.BusinessId=j.BusinessId
            INNER JOIN dbo.DocumentProcessingPayloads p
              ON p.DocumentId=j.DocumentId AND p.DocumentType=j.DocumentType
            INNER JOIN dbo.Businesses b ON b.BusinessId=j.BusinessId
            WHERE j.JobId=@MovementId AND j.BusinessId=@BusinessId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@MovementId", signal.MovementId);
        command.Parameters.AddWithValue("@BusinessId", signal.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new(DocumentProcessingWorkState.Missing, null,
                "The movement referenced by the broker message does not exist.");

        var status = reader.GetString(0);
        var sequence = reader.GetInt64(1);
        var lastCompleted = reader.GetInt64(2);
        var documentId = reader.GetGuid(7);
        var documentType = reader.GetString(8);
        if (documentId != signal.DocumentId ||
            !string.Equals(documentType, signal.DocumentType, StringComparison.Ordinal))
            return new(DocumentProcessingWorkState.Missing, null,
                "The broker message does not match the persisted movement.");
        if (status == "Completed")
            return new(DocumentProcessingWorkState.Completed, null);
        if (status == "NeedsIntervention")
            return new(DocumentProcessingWorkState.NotReady, null,
                "The movement requires intervention and blocks its business stream.");
        if (sequence != lastCompleted + 1)
            return new(DocumentProcessingWorkState.NotReady, null,
                "An earlier movement in the business stream must complete first.");

        var now = timeProvider.GetUtcNow();
        var availableAt = reader.GetDateTimeOffset(3);
        DateTimeOffset? leaseExpiresAt = reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4);
        if (availableAt > now || status == "Processing" && leaseExpiresAt > now)
            return new(DocumentProcessingWorkState.NotReady, null,
                "The movement is not available for this delivery yet.");

        return new(
            DocumentProcessingWorkState.Ready,
            new ConfirmedDocument(
                new TenantId(reader.GetGuid(5)),
                new BusinessId(reader.GetGuid(6)),
                new DocumentId(documentId),
                documentType,
                reader.GetString(9),
                reader.GetDateTimeOffset(10)));
    }
}
