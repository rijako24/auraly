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
    internal const string EconomicEffectsColumn = """
        CAST(CASE WHEN j.DocumentType IN (N'PosSale',N'SalesInvoice',N'SalesReceipt')
            AND (JSON_VALUE(p.PayloadJson,'$.fiscalHabilitationOnly')=N'true'
              OR JSON_VALUE(p.PayloadJson,'$.FiscalHabilitationOnly')=N'true')
            THEN 0 ELSE 1 END AS bit)
        """;

    private const string PendingEffectsPredicate = """
        EXISTS
        (
          SELECT 1 FROM dbo.ServerOutboxMessages message
          WHERE message.DocumentId=j.DocumentId
            AND message.DocumentType=j.DocumentType
            AND message.ProcessedAt IS NULL
            AND message.Type<>N'FiscalDocument.DianAccepted'
        )
        """;

    public async Task<DocumentProcessingWork> LoadAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT j.Status,j.ProcessingSequence,c.LastCompletedSequence,
                   j.AvailableAt,j.LeaseExpiresAt,
                   b.TenantId,j.BusinessId,j.DocumentId,j.DocumentType,
                   p.PayloadJson,p.AcceptedAt,
                   {EconomicEffectsColumn} AS EconomicEffectsEnabled,
                   CAST(CASE WHEN j.Status=N'Completed' AND {PendingEffectsPredicate}
                             THEN 1 ELSE 0 END AS bit) AS HasPendingEffects,
                   j.AttemptCount
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
        if (status == "DeadLettered")
            return new(DocumentProcessingWorkState.DeadLettered, null);
        var economicEffects = reader.GetBoolean(11);
        if (status == "Completed")
            return reader.GetBoolean(12)
                ? new(DocumentProcessingWorkState.Completed, null, EconomicEffectsEnabled: economicEffects)
                : new(DocumentProcessingWorkState.CompletedAndPublished, null);
        if (status == "NeedsIntervention")
            return new(DocumentProcessingWorkState.NotReady, null,
                "The movement requires intervention and blocks its business stream.");
        if (sequence != lastCompleted + 1)
            return new(DocumentProcessingWorkState.NotReady, null,
                "An earlier movement in the business stream must complete first.");

        var now = timeProvider.GetUtcNow();
        var availableAt = reader.GetDateTimeOffset(3);
        var retry = status == "RetryScheduled"
            ? new DocumentProcessingRetry(reader.GetInt32(13), availableAt)
            : null;
        DateTimeOffset? leaseExpiresAt = reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4);
        if (availableAt > now || status == "Processing" && leaseExpiresAt > now)
            return new(DocumentProcessingWorkState.NotReady, null,
                "The movement is not available for this delivery yet.",
                EconomicEffectsEnabled: economicEffects, Retry: retry);

        return new(
            DocumentProcessingWorkState.Ready,
            new ConfirmedDocument(
                new TenantId(reader.GetGuid(5)),
                new BusinessId(reader.GetGuid(6)),
                new DocumentId(documentId),
                documentType,
                reader.GetString(9),
                reader.GetDateTimeOffset(10)),
            EconomicEffectsEnabled: economicEffects, Retry: retry);
    }
}
