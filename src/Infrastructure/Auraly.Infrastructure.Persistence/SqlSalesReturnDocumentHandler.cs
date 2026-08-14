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
        await InsertTaxSummariesAsync(session, value, cancellationToken);
        await ApplyEconomicResolutionAsync(session, value, cancellationToken);
        await InsertFiscalWorkAsync(session, value, cancellationToken);
        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, value.ReturnedAt, ids, timeProvider, cancellationToken);
        await InsertOutboxAsync(session, value, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, value, cancellationToken);
    }

    private async Task ApplyInventoryAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        SalesReturnLineSnapshot line,
        CancellationToken cancellationToken)
    {
        if (line.InventoryDisposition != ReturnInventoryDispositions.Sellable) return;
        const string sql = """
            DECLARE @ManageStock BIT;
            DECLARE @InventoryFactor DECIMAL(19,6)=1;
            SELECT @ProductId=l.ParentProductId,@InventoryFactor=l.InventoryFactor
            FROM dbo.ProductLinks l WITH(UPDLOCK,HOLDLOCK)
            WHERE l.BusinessId=@BusinessId AND l.ChildProductId=@ProductId AND l.SharesInventory=1 AND l.IsActive=1;
            SET @Quantity=CAST(@Quantity*@InventoryFactor AS DECIMAL(19,6));

            SET @RecognizedUnitCost=CAST(@RecognizedUnitCost/@InventoryFactor AS DECIMAL(19,6));
            DECLARE @QuantityBefore DECIMAL(19,6);
            DECLARE @AverageCost DECIMAL(19,6);
            DECLARE @ValueBefore DECIMAL(19,4);
            SELECT @ManageStock=p.ManageStock
            FROM dbo.Products p WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Warehouses w WITH (UPDLOCK,HOLDLOCK)
              ON w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId
            WHERE p.ProductId=@ProductId AND p.BusinessId=@BusinessId;
            IF @ManageStock IS NULL
              THROW 51210,'The return product or warehouse is outside the business.',1;
            IF @ManageStock=0 RETURN;
            IF NOT EXISTS
              (SELECT 1 FROM dbo.InventoryBalances WITH (UPDLOCK,HOLDLOCK)
               WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId)
              INSERT dbo.InventoryBalances
                (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                 InventoryValue,LastProcessingSequence,UpdatedAt)
              VALUES(@BusinessId,@WarehouseId,@ProductId,0,0,0,@Sequence,@Now);
            SELECT @QuantityBefore=QuantityOnHand,@AverageCost=AverageUnitCost,
                   @ValueBefore=InventoryValue
            FROM dbo.InventoryBalances WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            DECLARE @QuantityAfter DECIMAL(19,6)=@QuantityBefore+@Quantity;
            DECLARE @ValueChange DECIMAL(19,4)=CAST(@Quantity*@RecognizedUnitCost AS DECIMAL(19,4));
            DECLARE @ValueAfter DECIMAL(19,4)=@ValueBefore+@ValueChange;
            DECLARE @AverageCostAfter DECIMAL(19,6)=CASE WHEN @QuantityAfter=0 THEN 0
              ELSE CAST(@ValueAfter/@QuantityAfter AS DECIMAL(19,6)) END;
            UPDATE dbo.InventoryBalances
            SET QuantityOnHand=@QuantityAfter,InventoryValue=@ValueAfter,AverageUnitCost=@AverageCostAfter,
                LastProcessingSequence=@Sequence,UpdatedAt=@Now
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            INSERT dbo.InventoryMovements
              (InventoryMovementId,BusinessId,WarehouseId,DocumentId,DocumentType,
               LineNumber,ProductId,MovementType,QuantityChange,ProcessingSequence,
               QuantityBefore,QuantityAfter,AverageUnitCostBefore,AverageUnitCostAfter,
               RecognizedUnitCost,ValueChange,OccurredAt,PostedAt,CreatedAt)
            VALUES(@MovementId,@BusinessId,@WarehouseId,@DocumentId,N'SalesReturn',
               @LineNumber,@ProductId,N'SalesReturn',@Quantity,@Sequence,@QuantityBefore,
               @QuantityAfter,@AverageCost,@AverageCostAfter,@RecognizedUnitCost,@ValueChange,
               @OccurredAt,@Now,@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MovementId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", value.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        command.Parameters.AddWithValue("@DocumentId", value.ReturnId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
        AddDecimal(command, "@RecognizedUnitCost", line.RecognizedUnitCost, 19, 6);
        command.Parameters.AddWithValue("@Sequence", session.ProcessingSequence);
        command.Parameters.AddWithValue("@OccurredAt", value.ReturnedAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    private async Task InsertTaxSummariesAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.SalesReturnTaxSummaries
              (ReturnId,TaxCode,TaxRate,TaxableAmount,TaxAmount,TotalAmount,CreatedAt)
            VALUES(@ReturnId,@TaxCode,@TaxRate,@Taxable,@Tax,@Total,@Now);
            """;
        foreach (var summary in value.Lines
            .GroupBy(line => new { line.TaxCode, line.TaxRate })
            .OrderBy(group => group.Key.TaxCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TaxRate))
        {
            await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            command.Parameters.AddWithValue("@TaxCode", summary.Key.TaxCode);
            AddDecimal(command, "@TaxRate", summary.Key.TaxRate, 9, 6);
            AddDecimal(command, "@Taxable", summary.Sum(line => line.UntaxedAmount), 19, 4);
            AddDecimal(command, "@Tax", summary.Sum(line => line.TaxAmount), 19, 4);
            AddDecimal(command, "@Total", summary.Sum(line => line.LineTotal), 19, 4);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
    private async Task ApplyEconomicResolutionAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        const string settlement = """
            INSERT dbo.SalesReturnSettlements
              (ReturnId,SettlementNumber,SettlementType,MethodCode,OriginalDocumentId,
               OriginalPaymentNumber,Amount,Reference,OccurredAt)
            VALUES(@ReturnId,1,@Type,@Method,@OriginalDocumentId,@PaymentNumber,@Amount,@Reference,@OccurredAt);
            """;
        await using (var command = new SqlCommand(
            settlement, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            command.Parameters.AddWithValue("@Type", value.EconomicResolution);
            command.Parameters.AddWithValue("@Method", (object?)value.RefundMethodCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@OriginalDocumentId", value.OriginalDocumentId);
            command.Parameters.AddWithValue("@PaymentNumber", (object?)value.OriginalPaymentNumber ?? DBNull.Value);
            AddDecimal(command, "@Amount", value.TotalAmount, 19, 4);
            command.Parameters.AddWithValue("@Reference", value.DocumentNumber);
            command.Parameters.AddWithValue("@OccurredAt", value.ReturnedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (value.EconomicResolution == ReturnEconomicResolutions.Refund)
        {
            await InsertCashRefundAsync(session, value, cancellationToken);
            return;
        }
        if (value.CustomerId is null)
            throw new InvalidOperationException(
                "An identified customer is required to open customer credit.");
        var remaining = await ApplyToReceivableAsync(session, value, cancellationToken);
        if (remaining <= 0) return;
        const string credit = """
            INSERT dbo.CustomerCredits
              (CustomerCreditId,BusinessId,CustomerId,SourceReturnId,OriginalAmount,
               AvailableAmount,Status,CreatedAt)
            VALUES(@Id,@BusinessId,@CustomerId,@ReturnId,@Amount,@Amount,N'Open',@Now);
            """;
        await using var creditCommand = new SqlCommand(
            credit, session.Connection, session.Transaction);
        creditCommand.Parameters.AddWithValue("@Id", ids.NewId());
        creditCommand.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        creditCommand.Parameters.AddWithValue("@CustomerId", value.CustomerId.Value);
        creditCommand.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        AddDecimal(creditCommand, "@Amount", remaining, 19, 4);
        creditCommand.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await creditCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertCashRefundAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        if (value.WorkSessionId is null || value.OriginalPaymentNumber is null)
            throw new InvalidOperationException(
                "A cash refund requires its active work session and original cash payment.");
        const string sql = """
            INSERT dbo.WorkSessionMovements
              (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,BusinessDate,
               MovementType,PaymentMethodCode,Amount,Reference,SourceKey,OccurredAt,RecordedByUserId)
            VALUES(@Id,@SessionId,@DocumentId,@PaymentNumber,@BusinessDate,
                   N'Refund',N'Cash',@Amount,@Reference,@SourceKey,@OccurredAt,@UserId);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@SessionId", value.WorkSessionId.Value);
        command.Parameters.AddWithValue("@DocumentId", value.OriginalDocumentId);
        command.Parameters.AddWithValue("@PaymentNumber", value.OriginalPaymentNumber.Value);
        command.Parameters.Add(new SqlParameter("@BusinessDate", SqlDbType.Date)
            { Value = value.ReturnedAt.Date });
        AddDecimal(command, "@Amount", -value.TotalAmount, 19, 4);
        command.Parameters.AddWithValue("@Reference", value.DocumentNumber);
        command.Parameters.AddWithValue("@SourceKey", $"sales-return:{value.ReturnId:N}");
        command.Parameters.AddWithValue("@OccurredAt", value.ReturnedAt);
        command.Parameters.AddWithValue("@UserId", value.CreatedByUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<decimal> ApplyToReceivableAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        const string loadSql = """
            SELECT TOP(1) ReceivableId,OutstandingAmount
            FROM dbo.Receivables WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND CustomerId=@CustomerId
              AND SourceDocumentId=@DocumentId AND SourceDocumentType=N'SalesInvoice'
              AND Status IN(N'Open',N'PartiallyPaid')
            ORDER BY CreatedAt;
            """;
        Guid receivableId;
        decimal outstanding;
        await using (var load = new SqlCommand(loadSql, session.Connection, session.Transaction))
        {
            load.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            load.Parameters.AddWithValue("@CustomerId", value.CustomerId!.Value);
            load.Parameters.AddWithValue("@DocumentId", value.OriginalDocumentId);
            await using var reader = await load.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return value.TotalAmount;
            receivableId = reader.GetGuid(0);
            outstanding = reader.GetDecimal(1);
        }
        var applied = decimal.Min(value.TotalAmount, outstanding);
        if (applied <= 0) return value.TotalAmount;
        var remainingOutstanding = outstanding - applied;
        const string applySql = """
            UPDATE dbo.Receivables
            SET OutstandingAmount=@Outstanding,
                Status=CASE WHEN @Outstanding=0 THEN N'Paid' ELSE N'PartiallyPaid' END
            WHERE ReceivableId=@ReceivableId;
            INSERT dbo.ReceivableTransactions
              (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
               SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@ReceivableId,N'Reversal',@Amount,
                   @ReturnId,@OccurredAt,@Now);
            INSERT dbo.SalesReturnReceivableApplications
              (ReturnId,ReceivableId,Amount,AppliedAt)
            VALUES(@ReturnId,@ReceivableId,@Amount,@Now);
            """;
        await using var command = new SqlCommand(applySql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@ReceivableId", receivableId);
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        AddDecimal(command, "@Outstanding", remainingOutstanding, 19, 4);
        AddDecimal(command, "@Amount", applied, 19, 4);
        command.Parameters.AddWithValue("@OccurredAt", value.ReturnedAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return value.TotalAmount - applied;
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
