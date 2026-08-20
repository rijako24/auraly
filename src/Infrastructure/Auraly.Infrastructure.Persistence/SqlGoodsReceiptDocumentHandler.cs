using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Inventory;
using Auraly.Domain.Payables;
using Auraly.Domain.Pricing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlGoodsReceiptDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    SqlInventoryLedgerWriter inventoryWriter,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => PurchasingDocumentTypes.GoodsReceipt;

    public async Task HandleAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var receipt = GoodsReceiptContractSerializer.Deserialize(document.Payload);
        if (receipt.DocumentId != document.DocumentId.Value ||
            receipt.BusinessId != document.BusinessId.Value ||
            receipt.TenantId != document.TenantId.Value)
            throw new InvalidOperationException("The goods receipt envelope does not match its payload.");

        if (receipt.Withholding.GrossAmount != receipt.GrandTotal ||
            receipt.Withholding.WithholdingTotal != receipt.Withholding.Lines.Sum(line => line.Amount) ||
            receipt.Withholding.NetAmount + receipt.Withholding.WithholdingTotal != receipt.GrandTotal)
            throw new InvalidOperationException("The immutable withholding snapshot does not reconcile.");

        var session = sessions.Current;
        foreach (var line in receipt.Lines.OrderBy(line => line.LineNumber))
            await ProcessLineAsync(session, receipt, line, cancellationToken);
        await PersistWithholdingSnapshotAsync(session, receipt, cancellationToken);
        if (receipt.CreatesPayable && receipt.Withholding.NetAmount > 0)
            await OpenPayableAsync(session, receipt, cancellationToken);
        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, receipt.ReceivedAt, ids, timeProvider,
            cancellationToken);
        await InsertOutboxAsync(session, receipt, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, receipt, cancellationToken);
    }

    private async Task PersistWithholdingSnapshotAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        CancellationToken cancellationToken)
    {
        await using (var command = new SqlCommand("""
            INSERT dbo.DocumentWithholdingSnapshots
              (DocumentId,DocumentType,BusinessId,GrossAmount,WithholdingTotal,NetAmount,RecognizedAt)
            VALUES(@DocumentId,N'GoodsReceipt',@BusinessId,@Gross,@Withholding,@Net,@At);
            """, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
            AddDecimal(command, "@Gross", receipt.Withholding.GrossAmount, 19, 4);
            AddDecimal(command, "@Withholding", receipt.Withholding.WithholdingTotal, 19, 4);
            AddDecimal(command, "@Net", receipt.Withholding.NetAmount, 19, 4);
            command.Parameters.AddWithValue("@At", receipt.ReceivedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0; index < receipt.Withholding.Lines.Count; index++)
        {
            var line = receipt.Withholding.Lines[index];
            await using var command = new SqlCommand("""
                INSERT dbo.DocumentWithholdingLines
                  (DocumentId,DocumentType,LineNumber,RuleId,RuleVersion,RuleCode,Name,Kind,
                   BaseKind,TaxableBase,Rate,Amount,JurisdictionCode)
                VALUES(@DocumentId,N'GoodsReceipt',@Line,@RuleId,@Version,@Code,@Name,@Kind,
                   @BaseKind,@Base,@Rate,@Amount,@Jurisdiction);
                """, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
            command.Parameters.AddWithValue("@Line", index + 1);
            command.Parameters.AddWithValue("@RuleId", line.RuleId);
            command.Parameters.AddWithValue("@Version", line.RuleVersion);
            command.Parameters.AddWithValue("@Code", line.RuleCode);
            command.Parameters.AddWithValue("@Name", line.Name);
            command.Parameters.AddWithValue("@Kind", line.Kind);
            command.Parameters.AddWithValue("@BaseKind", line.BaseKind);
            AddDecimal(command, "@Base", line.TaxableBase, 19, 4);
            AddDecimal(command, "@Rate", line.Rate, 9, 6);
            AddDecimal(command, "@Amount", line.Amount, 19, 4);
            command.Parameters.AddWithValue("@Jurisdiction", (object?)line.JurisdictionCode ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task ProcessLineAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        GoodsReceiptLineSnapshot line,
        CancellationToken cancellationToken)
    {
        var inventoryTarget = await SqlProductLinkResolution.ResolveInventoryAsync(session, receipt.BusinessId, line.ProductId, cancellationToken);
        var inventoryLine = line with { ProductId = inventoryTarget.ProductId, Quantity = line.Quantity * inventoryTarget.Factor };
        var state = await LoadLineStateAsync(session, receipt, line, inventoryTarget.ProductId, cancellationToken);
        var acquisitionAmount = line.NetAmount +
            (line.TaxTreatment == PurchasingTaxTreatments.CapitalizedCost
                ? line.TaxAmount
                : 0m);
        var acquisitionUnitCost = decimal.Round(
            acquisitionAmount / line.Quantity,
            6,
            MidpointRounding.AwayFromZero);
        var priceFormationCost = acquisitionUnitCost;
        if (state.ManageStock)
        {
            var projectedValuation = WeightedAverageCost.ApplyReceipt(
                state.QuantityOnHand, state.InventoryValue, inventoryLine.Quantity,
                acquisitionUnitCost / inventoryTarget.Factor);
            await ApplyInventoryReceiptAsync(
                session, receipt, inventoryLine, acquisitionUnitCost / inventoryTarget.Factor, cancellationToken);
            if (state.PriceFormationCostBasis == "WeightedAverageCost")
                priceFormationCost = projectedValuation.AverageUnitCostAfter * inventoryTarget.Factor;
        }
        await RecordSupplierCostAsync(
            session, receipt, line, acquisitionUnitCost, state.PreviousObservedUnitCost,
            cancellationToken);
        await CreatePriceProposalAsync(
            session, receipt, line, priceFormationCost, state, cancellationToken);
    }

    private static async Task<ReceiptLineState> LoadLineStateAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        GoodsReceiptLineSnapshot line,
        Guid inventoryProductId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT inventoryProduct.ManageStock,pp.Amount,lc.LatestUnitCost,
                   ib.QuantityOnHand,ib.AverageUnitCost,ib.InventoryValue,
                   COALESCE(tax.Rate,0),w.PriceFormationCostBasis,pp.TargetMarginPercent,
                   COALESCE(pp.RoundingIncrement,1),COALESCE(pp.RoundingMode,N'Nearest')
            FROM dbo.Products p WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Warehouses w WITH (UPDLOCK,HOLDLOCK)
              ON w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId
            INNER JOIN dbo.SupplierProducts sp WITH (UPDLOCK,HOLDLOCK)
              ON sp.ProductId=p.ProductId AND sp.SupplierId=@SupplierId
             AND sp.BusinessId=@BusinessId AND sp.IsActive=1
            INNER JOIN dbo.Products inventoryProduct WITH (UPDLOCK,HOLDLOCK)
              ON inventoryProduct.ProductId=@InventoryProductId AND inventoryProduct.BusinessId=p.BusinessId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=p.TaxProfileId AND tax.BusinessId=p.BusinessId
            INNER JOIN dbo.ProductPrices pp WITH (UPDLOCK,HOLDLOCK)
              ON pp.ProductId=p.ProductId AND pp.BusinessId=@BusinessId AND pp.IsActive=1
            LEFT JOIN dbo.SupplierProductLatestCosts lc WITH (UPDLOCK,HOLDLOCK)
              ON lc.BusinessId=@BusinessId AND lc.SupplierId=@SupplierId AND lc.ProductId=p.ProductId
            LEFT JOIN dbo.InventoryBalances ib WITH (UPDLOCK,HOLDLOCK)
              ON ib.BusinessId=@BusinessId AND ib.WarehouseId=@WarehouseId AND ib.ProductId=inventoryProduct.ProductId
            WHERE p.ProductId=@ProductId AND p.BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", receipt.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", receipt.SupplierId);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        command.Parameters.AddWithValue("@InventoryProductId", inventoryProductId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "The receipt product, price, supplier association or warehouse is no longer valid.");
        return new ReceiptLineState(
            reader.GetBoolean(0),
            reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
            reader.IsDBNull(4) ? 0 : reader.GetDecimal(4),
            reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
            !reader.IsDBNull(3),
            reader.GetDecimal(6),reader.GetString(7),reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.GetDecimal(9),reader.GetString(10));
    }

    private Task ApplyInventoryReceiptAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        GoodsReceiptLineSnapshot line,
        decimal acquisitionUnitCost,
        CancellationToken cancellationToken) =>
        inventoryWriter.PostAsync(
            session,
            new InventoryLedgerPosting(
                receipt.BusinessId,
                receipt.WarehouseId,
                line.ProductId,
                receipt.DocumentId,
                PurchasingDocumentTypes.GoodsReceipt,
                line.LineNumber,
                "GoodsReceipt",
                line.Quantity,
                acquisitionUnitCost,
                InventoryValuationModes.WeightedAverageReceipt,
                receipt.ReceivedAt),
            cancellationToken);

    private async Task RecordSupplierCostAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        GoodsReceiptLineSnapshot line,
        decimal acquisitionUnitCost,
        decimal? previousCost,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.SupplierCostObservations
              (SupplierCostObservationId,BusinessId,SupplierId,ProductId,SourceDocumentId,
               SourceLineNumber,UnitCost,CurrencyCode,ObservedAt,CreatedAt)
            VALUES(@ObservationId,@BusinessId,@SupplierId,@ProductId,@DocumentId,
               @LineNumber,@UnitCost,@Currency,@ObservedAt,@Now);

            IF EXISTS (SELECT 1 FROM dbo.SupplierProductLatestCosts WITH (UPDLOCK,HOLDLOCK)
                       WHERE BusinessId=@BusinessId AND SupplierId=@SupplierId AND ProductId=@ProductId)
              UPDATE dbo.SupplierProductLatestCosts
              SET PreviousUnitCost=LatestUnitCost,LatestUnitCost=@UnitCost,CurrencyCode=@Currency,
                  SourceDocumentId=@DocumentId,SourceLineNumber=@LineNumber,ObservedAt=@ObservedAt
              WHERE BusinessId=@BusinessId AND SupplierId=@SupplierId AND ProductId=@ProductId;
            ELSE
              INSERT dbo.SupplierProductLatestCosts
                (BusinessId,SupplierId,ProductId,PreviousUnitCost,LatestUnitCost,CurrencyCode,
                 SourceDocumentId,SourceLineNumber,ObservedAt)
              VALUES(@BusinessId,@SupplierId,@ProductId,@PreviousCost,@UnitCost,@Currency,
                 @DocumentId,@LineNumber,@ObservedAt);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@ObservationId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
        command.Parameters.AddWithValue("@SupplierId", receipt.SupplierId);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        AddDecimal(command, "@UnitCost", acquisitionUnitCost, 19, 6);
        command.Parameters.AddWithValue("@Currency", receipt.CurrencyCode);
        command.Parameters.AddWithValue("@ObservedAt", receipt.ReceivedAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        var previous = command.Parameters.Add("@PreviousCost", SqlDbType.Decimal);
        previous.Precision = 19;
        previous.Scale = 6;
        previous.Value = (object?)previousCost ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CreatePriceProposalAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        GoodsReceiptLineSnapshot line,
        decimal observedCost,
        ReceiptLineState state,
        CancellationToken cancellationToken)
    {
        if (state.TargetMarginPercent is null or <= 0 or >= 100)
            throw new InvalidOperationException("The product requires a valid target margin before receiving merchandise.");
        var currentMargin = observedCost <= 0 ? null : PriceMargin.CalculateMarginPercentFromGross(
            observedCost, state.CurrentSalePrice, state.SalesTaxRate);
        var targetMargin = state.TargetMarginPercent.Value;
        var rawSuggested = PriceMargin.CalculateGrossSalePrice(observedCost, targetMargin, state.SalesTaxRate);
        var suggested = PriceMargin.RoundPrice(rawSuggested, state.RoundingIncrement, state.RoundingMode);
        var effectiveMargin = PriceMargin.CalculateMarginPercentFromGross(
            observedCost, suggested, state.SalesTaxRate);
        var costBasisType = state.PriceFormationCostBasis == "WeightedAverageCost"
            ? "WeightedAverageCost"
            : "LatestReceiptCost";
        const string sql = """
            UPDATE dbo.ProductPrices
            SET CostBasisType=@CostBasisType,CostBasisAmount=@ObservedCost,PreparedAmount=@RoundedPrice,
                TargetMarginPercent=@TargetMargin,EffectiveMarginPercent=@EffectiveMargin,
                InputMode=N'Margin'
            WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;

            INSERT dbo.PriceRevisionProposals
              (PriceRevisionProposalId,BusinessId,ProductId,SourceDocumentId,SourceLineNumber,
               PreviousObservedUnitCost,ObservedUnitCost,CurrentSalePrice,CurrentMarginPercent,
               TargetMarginPercent,SuggestedSalePrice,RoundedSuggestedSalePrice,
               EffectiveMarginAfterRounding,LastInputMode,Status,CreatedAt)
            VALUES(@Id,@BusinessId,@ProductId,@DocumentId,@LineNumber,@PreviousCost,@ObservedCost,
               @CurrentPrice,@CurrentMargin,@TargetMargin,@RawPrice,@RoundedPrice,
               @EffectiveMargin,N'Margin',N'PendingReview',@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        AddNullableDecimal(command, "@PreviousCost", state.PreviousObservedUnitCost, 19, 6);
        AddDecimal(command, "@ObservedCost", observedCost, 19, 6);
        AddDecimal(command, "@CurrentPrice", state.CurrentSalePrice, 19, 4);
        AddNullableDecimal(command, "@CurrentMargin", currentMargin, 9, 6);
        AddDecimal(command, "@TargetMargin", targetMargin, 9, 6);
        AddDecimal(command, "@RawPrice", rawSuggested, 19, 4);
        AddDecimal(command, "@RoundedPrice", suggested, 19, 4);
        AddNullableDecimal(command, "@EffectiveMargin", effectiveMargin, 9, 6);
        command.Parameters.AddWithValue("@CostBasisType", costBasisType);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task OpenPayableAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        CancellationToken cancellationToken)
    {
        var payable = PayableOpening.Create(receipt.Withholding.NetAmount, receipt.ReceivedAt, receipt.DueDate);
        var payableId = ids.NewId();
        var now = timeProvider.GetUtcNow();
        const string sql = """
            INSERT dbo.Payables
              (PayableId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,DocumentNumber,
               CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            VALUES(@PayableId,@BusinessId,@SupplierId,@DocumentId,N'GoodsReceipt',@Number,
               @Currency,@Original,@Outstanding,@DueDate,N'Open',@Now);
            INSERT dbo.PayableTransactions
              (PayableTransactionId,PayableId,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@PayableId,N'Opening',@Original,@DocumentId,@OccurredAt,@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@PayableId", payableId);
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
        command.Parameters.AddWithValue("@SupplierId", receipt.SupplierId);
        command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
        command.Parameters.AddWithValue("@Number", receipt.DocumentNumber);
        command.Parameters.AddWithValue("@Currency", receipt.CurrencyCode);
        AddDecimal(command, "@Original", payable.OriginalAmount, 19, 4);
        AddDecimal(command, "@Outstanding", payable.OutstandingAmount, 19, 4);
        command.Parameters.AddWithValue("@DueDate", payable.DueDate);
        command.Parameters.AddWithValue("@OccurredAt", receipt.ReceivedAt);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertOutboxAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        string payload,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.ServerOutboxMessages
              (MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            VALUES(@Id,@DocumentId,N'GoodsReceipt',N'purchasing.goods-receipt.processed',@Payload,@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkProcessedAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.GoodsReceipts SET Status=N'Processed',ProcessedAt=@Now
            WHERE GoodsReceiptId=@DocumentId AND BusinessId=@BusinessId AND Status=N'Accepted';
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DBConcurrencyException("The goods receipt could not be marked as processed.");
    }

    private static void AddDecimal(SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

    private static void AddNullableDecimal(SqlCommand command, string name, decimal? value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = (object?)value ?? DBNull.Value;
    }

    private sealed record ReceiptLineState(
        bool ManageStock,
        decimal CurrentSalePrice,
        decimal? PreviousObservedUnitCost,
        decimal QuantityOnHand,
        decimal AverageUnitCost,
        decimal InventoryValue,
        bool HasInventoryBalance,
        decimal SalesTaxRate,
        string PriceFormationCostBasis,
        decimal? TargetMarginPercent,
        decimal RoundingIncrement,
        string RoundingMode);
}
