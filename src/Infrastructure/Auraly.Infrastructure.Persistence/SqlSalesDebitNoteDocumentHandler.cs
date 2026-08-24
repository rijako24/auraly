using System.Data;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesDebitNoteDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => SalesDebitNoteDocumentTypes.SalesDebitNote;

    public async Task HandleAsync(ConfirmedDocument document, CancellationToken cancellationToken)
    {
        var value = SalesDebitNoteContractSerializer.Deserialize(document.Payload);
        if (value.DebitNoteId != document.DocumentId.Value ||
            value.BusinessId != document.BusinessId.Value || value.TenantId != document.TenantId.Value)
            throw new InvalidOperationException("The sales debit-note envelope does not match its payload.");
        var session = sessions.Current;
        await InsertFiscalWorkAsync(session, value, cancellationToken);
        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, value.IssuedAt, ids, timeProvider, cancellationToken);
        await InsertOutboxAsync(session, value, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, value, cancellationToken);
    }

    private static async Task InsertFiscalWorkAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesDebitNoteDocumentPayload value,
        CancellationToken cancellationToken)
    {
        const string loadSql = """
            SELECT original.SnapshotJson,c.FiscalIssuerConfigurationId,c.Environment,
                   fiscalAuthorization.QrValidationUrl
            FROM dbo.FiscalSnapshots original
            INNER JOIN dbo.SalesDocuments sale
              ON sale.DocumentId=original.DocumentId AND sale.BusinessId=@BusinessId
            INNER JOIN dbo.FiscalAuthorizations fiscalAuthorization
              ON fiscalAuthorization.FiscalAuthorizationId=sale.FiscalAuthorizationId
            CROSS APPLY
            (
              SELECT TOP(1) configuration.FiscalIssuerConfigurationId,configuration.Environment
              FROM dbo.FiscalIssuerConfigurations configuration
              WHERE configuration.BusinessId=@BusinessId AND configuration.IsActive=1
                AND configuration.ValidFrom<=@IssuedAt
                AND (configuration.ValidTo IS NULL OR configuration.ValidTo>@IssuedAt)
              ORDER BY configuration.Version DESC
            ) c
            WHERE original.DocumentId=@OriginalDocumentId;
            """;
        string originalJson;
        Guid issuerConfigurationId;
        int environment;
        string qrValidationUrl;
        await using (var load = new SqlCommand(loadSql, session.Connection, session.Transaction))
        {
            load.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            load.Parameters.AddWithValue("@OriginalDocumentId", value.OriginalDocumentId);
            load.Parameters.AddWithValue("@IssuedAt", value.IssuedAt);
            await using var reader = await load.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "The original invoice or active fiscal issuer is unavailable for the debit note.");
            originalJson = reader.GetString(0);
            issuerConfigurationId = reader.GetGuid(1);
            environment = reader.GetByte(2);
            qrValidationUrl = reader.GetString(3);
        }
        var original = PosSaleContractSerializer.Deserialize(originalJson);
        var originalUbl = original.UblSnapshot
            ?? throw new InvalidOperationException("The original invoice lacks immutable UBL metadata.");
        var originalFiscal = original.FiscalSnapshot
            ?? throw new InvalidOperationException("The original document is not an electronic invoice.");
        var snapshot = new SalesDebitNoteFiscalSnapshot(
            value, issuerConfigurationId, value.DocumentNumber, originalUbl.CurrencyCode,
            environment, qrValidationUrl, originalUbl.Customer,
            originalFiscal.FiscalNumber, originalFiscal.Cufe,
            DateOnly.FromDateTime(originalFiscal.IssuedAt.Date));
        var snapshotJson = SalesDebitNoteFiscalSnapshotSerializer.Serialize(snapshot);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson));
        var now = DateTimeOffset.UtcNow;
        const string insertSql = """
            INSERT dbo.FiscalDocuments
              (DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
               AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,
               IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,N'SalesDebitNote',N'DebitNote',
               @Number,@Number,N'CUDE',NULL,@IssuedAt,@Status,@Now,@Now);
            INSERT dbo.SalesDebitNoteFiscalSnapshots
              (DocumentId,SnapshotJson,PayloadHash,Environment,CreatedAt)
            VALUES(@DocumentId,@SnapshotJson,@Hash,@Environment,@Now);
            INSERT dbo.FiscalDocumentProcesses
              (DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,
               AttemptCount,CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,@IssuerId,@Status,0,@Now,@Now);
            UPDATE dbo.SalesDebitNotes SET FiscalStatus=@Status
            WHERE DebitNoteId=@DocumentId AND BusinessId=@BusinessId;
            """;
        await using var insert = new SqlCommand(insertSql, session.Connection, session.Transaction);
        insert.Parameters.AddWithValue("@DocumentId", value.DebitNoteId);
        insert.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        insert.Parameters.AddWithValue("@Number", value.DocumentNumber);
        insert.Parameters.AddWithValue("@IssuedAt", value.IssuedAt);
        insert.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingGeneration);
        insert.Parameters.AddWithValue("@Now", now);
        insert.Parameters.AddWithValue("@SnapshotJson", snapshotJson);
        insert.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
        insert.Parameters.AddWithValue("@Environment", environment);
        insert.Parameters.AddWithValue("@IssuerId", issuerConfigurationId);
        if (await insert.ExecuteNonQueryAsync(cancellationToken) != 4)
            throw new InvalidOperationException("The debit-note fiscal work was not persisted atomically.");
    }

    private async Task InsertOutboxAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesDebitNoteDocumentPayload value,
        string payload,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.ServerOutboxMessages
              (MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            VALUES(@Id,@DocumentId,N'SalesDebitNote',N'sales.debit-note.processed',@Payload,@Now);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", value.DebitNoteId);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkProcessedAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesDebitNoteDocumentPayload value,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.SalesDebitNotes SET Status=N'Processed',ProcessedAt=@Now
            WHERE DebitNoteId=@Id AND BusinessId=@BusinessId AND Status=N'Accepted';
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", value.DebitNoteId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DBConcurrencyException("The sales debit note could not be marked as processed.");
    }
}
