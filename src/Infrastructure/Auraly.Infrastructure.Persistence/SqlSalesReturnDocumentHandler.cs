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

public sealed class SqlSalesReturnDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    SqlInventoryLedgerWriter inventoryWriter,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => SalesReturnDocumentTypes.SalesReturn;

    public async Task HandleAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var value = SalesReturnContractSerializer.Deserialize(document.Payload);
        if (value.ReturnId != document.DocumentId.Value ||
            value.BusinessId != document.BusinessId.Value ||
            value.TenantId != document.TenantId.Value)
            throw new InvalidOperationException(
                "The sales return envelope does not match its payload.");
        var session = sessions.Current;
        foreach (var line in value.Lines.OrderBy(line => line.LineNumber))
            await ApplyInventoryAsync(session, value, line, cancellationToken);
        await InsertFiscalWorkAsync(session, value, cancellationToken);
        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, value.ReturnedAt, ids, timeProvider, cancellationToken);
        await SqlSalesReportingJobWriter.InsertAsync(
            session, document, ids, timeProvider, cancellationToken);
        await InsertOutboxAsync(session, value, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, value, cancellationToken);
    }

    private Task ApplyInventoryAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        SalesReturnLineSnapshot line,
        CancellationToken cancellationToken)
    {
        if (line.InventoryDisposition != ReturnInventoryDispositions.Sellable)
            return Task.CompletedTask;

        return inventoryWriter.PostAsync(
            session,
            new InventoryLedgerPosting(
                value.BusinessId,
                value.WarehouseId,
                line.ProductId,
                value.ReturnId,
                SalesReturnDocumentTypes.SalesReturn,
                line.LineNumber,
                "SalesReturn",
                line.Quantity,
                line.RecognizedUnitCost,
                InventoryValuationModes.WeightedAverageReceipt,
                value.ReturnedAt),
            cancellationToken);
    }

    private static async Task InsertFiscalWorkAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
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
              SELECT TOP (1) configuration.FiscalIssuerConfigurationId,
                     configuration.Environment
              FROM dbo.FiscalIssuerConfigurations configuration
              WHERE configuration.BusinessId=@BusinessId
                AND configuration.IsActive=1
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
        await using (var load = new SqlCommand(
            loadSql, session.Connection, session.Transaction))
        {
            load.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            load.Parameters.AddWithValue("@OriginalDocumentId", value.OriginalDocumentId);
            load.Parameters.AddWithValue("@IssuedAt", value.ReturnedAt);
            await using var reader = await load.ExecuteReaderAsync(cancellationToken);
            // Sales receipts are deliberately non-fiscal. Their return still applies inventory
            // and the economic resolution, but it must not try to create a DIAN credit note.
            if (!await reader.ReadAsync(cancellationToken)) return;
            originalJson = reader.GetString(0);
            issuerConfigurationId = reader.GetGuid(1);
            environment = reader.GetByte(2);
            qrValidationUrl = reader.GetString(3);
        }

        var original = PosSaleContractSerializer.Deserialize(originalJson);
        var originalUbl = original.UblSnapshot
            ?? throw new InvalidOperationException(
                "The original invoice has no immutable UBL metadata for its credit note.");
        var originalMetadata = originalUbl.Lines.ToDictionary(line => line.LineNumber);
        var originalFiscal = original.FiscalSnapshot
            ?? throw new InvalidOperationException(
                "The original document is not an electronic invoice.");
        var lineMetadata = value.Lines.Select(line =>
        {
            if (!originalMetadata.TryGetValue(line.OriginalLineNumber, out var metadata))
                throw new InvalidOperationException(
                    $"The original fiscal snapshot has no metadata for line {line.OriginalLineNumber}.");
            return new CreditNoteLineMetadata(
                line.LineNumber, metadata.ProductCode, metadata.ProductCodeScheme,
                metadata.UnitCode, metadata.TaxName);
        }).ToArray();
        var snapshot = new SalesReturnCreditNoteSnapshot(
            value, issuerConfigurationId, value.DocumentNumber, originalUbl.CurrencyCode,
            environment, qrValidationUrl, originalUbl.Customer,
            originalFiscal.FiscalNumber, originalFiscal.Cufe,
            DateOnly.FromDateTime(originalFiscal.IssuedAt.Date), lineMetadata);
        var snapshotJson = SalesReturnCreditNoteSnapshotSerializer.Serialize(snapshot);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson));
        var now = DateTimeOffset.UtcNow;

        const string insertSql = """
            INSERT dbo.FiscalDocuments
              (DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
               AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,
               IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,N'SalesReturn',N'CreditNote',
               @Number,@Number,N'CUDE',NULL,@IssuedAt,@Status,@Now,@Now);
            INSERT dbo.SalesReturnFiscalSnapshots
              (DocumentId,SnapshotJson,PayloadHash,Environment,CreatedAt)
            VALUES(@DocumentId,@SnapshotJson,@Hash,@Environment,@Now);
            INSERT dbo.FiscalDocumentProcesses
              (DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,
               AttemptCount,CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,@IssuerId,@Status,0,@Now,@Now);
            UPDATE dbo.SalesReturns SET FiscalStatus=@Status
            WHERE ReturnId=@DocumentId AND BusinessId=@BusinessId;
            """;
        await using var insert = new SqlCommand(
            insertSql, session.Connection, session.Transaction);
        insert.Parameters.AddWithValue("@DocumentId", value.ReturnId);
        insert.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        insert.Parameters.AddWithValue("@Number", value.DocumentNumber);
        insert.Parameters.AddWithValue("@IssuedAt", value.ReturnedAt);
        insert.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingGeneration);
        insert.Parameters.AddWithValue("@Now", now);
        insert.Parameters.AddWithValue("@SnapshotJson", snapshotJson);
        insert.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
        insert.Parameters.AddWithValue("@Environment", environment);
        insert.Parameters.AddWithValue("@IssuerId", issuerConfigurationId);
        if (await insert.ExecuteNonQueryAsync(cancellationToken) != 4)
            throw new InvalidOperationException(
                "The sales return fiscal work was not persisted atomically.");
    }

    private async Task InsertOutboxAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        string payload,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.ServerOutboxMessages
              (MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            VALUES(@Id,@DocumentId,N'SalesReturn',N'sales.return.processed',@Payload,@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", value.ReturnId);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkProcessedAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.SalesReturns SET Status=N'Processed',ProcessedAt=@Now
            WHERE ReturnId=@ReturnId AND BusinessId=@BusinessId AND Status=N'Accepted';
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DBConcurrencyException("The sales return could not be marked as processed.");
    }

    private static void AddDecimal(
        SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }
}
