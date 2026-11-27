using System.Data;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal enum DocumentContinuationKind
{
    FiscalGeneration = 0,
    Accounting = 1,
    Reporting = 2
}

internal sealed record DocumentContinuationTarget(
    DocumentContinuationKind Kind, Guid DocumentId, string DocumentType, int Ordinal, long SourceVersion = 1);

internal sealed record DocumentContinuationDelivery(Guid MessageId, DocumentContinuationTarget Target);

public sealed class SqlDocumentContinuationOutbox(SqlServerConnectionFactory connections, TimeProvider time)
{
    private const string Scope = """
        job.JobId=@MovementId AND job.BusinessId=@BusinessId AND job.DocumentId=@DocumentId
        AND job.DocumentType=@DocumentType AND job.Status=N'Completed'
        """;
    private const string MessageScope = "message.DocumentId=@DocumentId AND message.DocumentType=@DocumentType";
    private const string DeliveryRow = "message.Type LIKE N'doc.send.%'";
    private const string OperationalRow = "message.Type NOT LIKE N'doc.send.%' AND message.Type<>N'FiscalDocument.DianAccepted'";
    private const string ReadPending = $"""
        SELECT message.MessageId,message.Payload
        FROM dbo.ServerOutboxMessages message
        WHERE {MessageScope} AND {DeliveryRow} AND message.ProcessedAt IS NULL
          AND EXISTS (SELECT 1 FROM dbo.DocumentProcessingJobs job WHERE {Scope});
        """;

    // Null means the operational event is pending and the destination set still needs to be resolved.
    internal Task<IReadOnlyList<DocumentContinuationDelivery>?> LoadAsync(
        DocumentProcessingSignal signal, CancellationToken ct) => ReadAsync(signal, null, ct);

    internal async Task<IReadOnlyList<DocumentContinuationDelivery>> SavePlanAsync(
        DocumentProcessingSignal signal, IReadOnlyList<DocumentContinuationTarget> targets, CancellationToken ct) =>
        await ReadAsync(signal, targets, ct)
            ?? throw new InvalidOperationException("No se pudo guardar el plan de publicación documental.");

    private async Task<IReadOnlyList<DocumentContinuationDelivery>?> ReadAsync(
        DocumentProcessingSignal signal, IReadOnlyList<DocumentContinuationTarget>? targets, CancellationToken ct)
    {
        const string pendingSource = $"""
            EXISTS (SELECT 1 FROM dbo.ServerOutboxMessages message
                WHERE {MessageScope} AND {OperationalRow} AND message.ProcessedAt IS NULL)
            AND NOT EXISTS (SELECT 1 FROM dbo.ServerOutboxMessages message
                WHERE {MessageScope} AND {DeliveryRow})
            """;
        const string readSql = $"""
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.DocumentProcessingJobs job WHERE {Scope})
                AND {pendingSource} THEN 1 ELSE 0 END AS bit);
            {ReadPending}
            """;
        const string writeSql = $"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.DocumentProcessingJobs job WITH (UPDLOCK,HOLDLOCK) WHERE {Scope})
                AND {pendingSource}
            BEGIN
                INSERT dbo.ServerOutboxMessages(MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
                SELECT NEWID(),@DocumentId,@DocumentType,
                    CONCAT(N'doc.send.',JSON_VALUE(item.value,'$.Kind'),N'.',
                        JSON_VALUE(item.value,'$.DocumentId'),N'.',JSON_VALUE(item.value,'$.DocumentType'),
                        N'.',JSON_VALUE(item.value,'$.SourceVersion')),
                    item.value,@Now
                FROM OPENJSON(@Targets) item;
            END;
            COMMIT TRANSACTION;
            SELECT CAST(0 AS bit);
            {ReadPending}
            """;

        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = CreateCommand(connection, signal, targets is null ? readSql : writeSql);
        if (targets is not null)
            command.Parameters.Add("@Targets", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(targets);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("No se pudo consultar el plan de publicación documental.");
        if (reader.GetBoolean(0))
            return null;

        await reader.NextResultAsync(ct);
        var pending = new List<DocumentContinuationDelivery>();
        while (await reader.ReadAsync(ct))
        {
            var target = JsonSerializer.Deserialize<DocumentContinuationTarget>(reader.GetString(1))
                ?? throw new InvalidOperationException("El destino de publicación documental es inválido.");
            pending.Add(new DocumentContinuationDelivery(reader.GetGuid(0), target));
        }
        return pending.OrderBy(delivery => delivery.Target.Ordinal).ToArray();
    }

    internal async Task AcknowledgeAsync(
        DocumentProcessingSignal signal, IReadOnlyList<Guid> published, Guid? failedMessageId,
        string? error, CancellationToken ct)
    {
        const string sql = $"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.DocumentProcessingJobs job WITH (UPDLOCK,HOLDLOCK) WHERE {Scope})
            BEGIN
                UPDATE message SET ProcessedAt=@Now,AttemptCount=AttemptCount+1,LastError=NULL
                FROM dbo.ServerOutboxMessages message
                JOIN OPENJSON(@Published) WITH (MessageId uniqueidentifier '$') accepted
                    ON accepted.MessageId=message.MessageId
                WHERE {MessageScope} AND {DeliveryRow} AND message.ProcessedAt IS NULL;

                UPDATE message SET AttemptCount=AttemptCount+1,LastError=@Error
                FROM dbo.ServerOutboxMessages message
                WHERE {MessageScope} AND {DeliveryRow} AND message.MessageId=@Failed
                    AND message.ProcessedAt IS NULL;

                IF NOT EXISTS (SELECT 1 FROM dbo.ServerOutboxMessages message
                    WHERE {MessageScope} AND {DeliveryRow} AND message.ProcessedAt IS NULL)
                    UPDATE message SET ProcessedAt=@Now,AttemptCount=AttemptCount+1,LastError=NULL,
                        RecoveryAvailableAt=NULL,RecoveryLeaseId=NULL
                    FROM dbo.ServerOutboxMessages message
                    WHERE {MessageScope} AND {OperationalRow} AND message.ProcessedAt IS NULL;
            END;
            COMMIT TRANSACTION;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = CreateCommand(connection, signal, sql);
        command.Parameters.Add("@Published", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(published);
        command.Parameters.Add("@Failed", SqlDbType.UniqueIdentifier).Value = (object?)failedMessageId ?? DBNull.Value;
        command.Parameters.Add("@Error", SqlDbType.NVarChar, 2000).Value = (object?)error ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(ct);
    }

    private SqlCommand CreateCommand(SqlConnection connection, DocumentProcessingSignal signal, string sql)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@MovementId", signal.MovementId);
        command.Parameters.AddWithValue("@DocumentId", signal.DocumentId);
        command.Parameters.Add("@DocumentType", SqlDbType.NVarChar, 64).Value = signal.DocumentType;
        command.Parameters.AddWithValue("@BusinessId", signal.BusinessId);
        command.Parameters.AddWithValue("@Now", time.GetUtcNow());
        return command;
    }
}
