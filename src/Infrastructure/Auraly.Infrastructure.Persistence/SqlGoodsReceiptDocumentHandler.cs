using Auraly.Commerce.Accounting.Infrastructure;
using System.Data;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Inventory;
using Auraly.Domain.Pricing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlGoodsReceiptDocumentHandler(
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

        var receiptFunctionalTotal = receipt.FunctionalGrandTotal == 0 && receipt.GrandTotal > 0
            ? receipt.GrandTotal : receipt.FunctionalGrandTotal;
        if (receipt.Withholding.GrossAmount != receiptFunctionalTotal ||
            receipt.Withholding.WithholdingTotal != receipt.Withholding.Lines.Sum(line => line.Amount) ||
            receipt.Withholding.NetAmount + receipt.Withholding.WithholdingTotal != receiptFunctionalTotal)
            throw new InvalidOperationException("The immutable withholding snapshot does not reconcile.");
        foreach (var costDocument in receipt.AdditionalCostDocuments ?? [])
            if (costDocument.Withholding.GrossAmount != costDocument.FunctionalGrandTotal ||
                costDocument.Withholding.WithholdingTotal != costDocument.Withholding.Lines.Sum(line => line.Amount) ||
                costDocument.Withholding.NetAmount + costDocument.Withholding.WithholdingTotal != costDocument.FunctionalGrandTotal)
                throw new InvalidOperationException("An additional-cost withholding snapshot does not reconcile.");

        var session = sessions.Current;
        await ProcessLinesAsync(session, receipt, cancellationToken);
        await ApplyPurchaseOrderFulfillmentAsync(session, receipt, cancellationToken);
        await PersistWithholdingSnapshotsAsync(session, receipt, cancellationToken);
        var accountingSources = new List<(bool CreatesPayable, SqlAccountingPostingJobWriter.Source Source)>
        {
            (receipt.CreatesPayable, new(receipt.TenantId, receipt.BusinessId, receipt.DocumentId,
                document.DocumentType, null, receipt.SupplierInvoiceDate ?? receipt.ReceivedAt, ids.NewId()))
        };
        foreach (var costDocument in receipt.AdditionalCostDocuments ?? [])
        {
            var payload = GoodsReceiptContractSerializer.SerializeCostDocument(
                new GoodsReceiptCostDocumentAccountingPayload(
                    receipt.TenantId, receipt.BusinessId, receipt.DocumentId, costDocument));
            accountingSources.Add((costDocument.CreatesPayable,
                new(receipt.TenantId, receipt.BusinessId, costDocument.CostDocumentId,
                    PurchasingDocumentTypes.GoodsReceiptCostDocument, payload, costDocument.IssuedAt, ids.NewId())));
        }
        var acceptedAt = timeProvider.GetUtcNow();
        foreach (var requirement in accountingSources.GroupBy(source => source.CreatesPayable))
            foreach (var batch in requirement.Chunk(100))
                await SqlAccountingPostingJobWriter.InsertSourcesAsync(
                    session.Connection, session.Transaction, batch.Select(source => source.Source).ToArray(),
                    acceptedAt, requirement.Key ? AccountingJobRequirement.PreserveCommercialEffects
                        : AccountingJobRequirement.AccountingEntryOnly, cancellationToken);
        await SqlSalesReportingJobWriter.InsertAsync(
            session, document, ids, timeProvider, cancellationToken);
        await InsertOutboxAsync(session, receipt, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, receipt, cancellationToken);
    }

    private async Task ApplyPurchaseOrderFulfillmentAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        CancellationToken cancellationToken)
    {
        if (receipt.PurchaseOrderId is null)
        {
            if (receipt.Lines.Any(line => line.PurchaseOrderLineId is not null))
                throw new InvalidOperationException("A receipt line references an order without an order header.");
            return;
        }

        if (receipt.Lines.Any(line => line.PurchaseOrderLineId is null))
            throw new InvalidOperationException("Every recovered receipt line requires an order-line reference.");

        await using var command = new SqlCommand(
            "purchasing.ReceiptOrderFulfillmentApply", session.Connection, session.Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
        command.Parameters.AddWithValue("@PurchaseOrderId", receipt.PurchaseOrderId.Value);
        command.Parameters.AddWithValue("@WarehouseId", receipt.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", receipt.SupplierId);
        command.Parameters.AddWithValue("@LinesJson", JsonSerializer.Serialize(receipt.Lines.Select(line => new
        {
            line.PurchaseOrderLineId,
            line.ProductId,
            line.Quantity,
            line.OverReceiptAuthorized
        })));
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Task PersistWithholdingSnapshotsAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt,
        CancellationToken cancellationToken)
    {
        var snapshots = (receipt.AdditionalCostDocuments ?? []).Select(document =>
            new SqlWithholdingRuleStore.DocumentSnapshotSource(document.CostDocumentId,
                PurchasingDocumentTypes.GoodsReceiptCostDocument, document.IssuedAt, document.Withholding))
            .Prepend(new(receipt.DocumentId, PurchasingDocumentTypes.GoodsReceipt,
                receipt.SupplierInvoiceDate ?? receipt.ReceivedAt, receipt.Withholding)).ToArray();
        return SqlWithholdingRuleStore.InsertDocumentSnapshotsAsync(session.Connection, session.Transaction,
            receipt.BusinessId, snapshots, cancellationToken);
    }

    private async Task ProcessLinesAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptDocumentPayload receipt, CancellationToken cancellationToken)
    {
        var lines = receipt.Lines.OrderBy(line => line.LineNumber).ToArray();
        var inventoryTargets = await SqlProductLinkResolution.ResolveInventoryBatchAsync(
            session, receipt.BusinessId, lines.Select(line => line.ProductId).ToArray(), cancellationToken);
        var states = await LoadLineStatesAsync(session, receipt.BusinessId, receipt.WarehouseId,
            receipt.SupplierId, inventoryTargets, cancellationToken);
        // Keep the original line order and per-line rounding, including receipts
        // containing several presentations of the same inventory product.
        var costs = lines.Select(line =>
        {
            var legacyPayload = receipt.FunctionalGrandTotal == 0m && receipt.GrandTotal > 0m;
            var recognized = legacyPayload ? line.NetAmount +
                (line.TaxTreatment == PurchasingTaxTreatments.CapitalizedCost ? line.TaxAmount : 0m)
                : line.RecognizedInventoryCostAmount;
            var supplier = legacyPayload ? recognized : line.FunctionalNetAmount +
                (line.TaxTreatment == PurchasingTaxTreatments.CapitalizedCost ? line.FunctionalTaxAmount : 0m);
            return (Acquisition: decimal.Round(recognized / line.Quantity, 6, MidpointRounding.AwayFromZero),
                Supplier: decimal.Round(supplier / line.Quantity, 6, MidpointRounding.AwayFromZero));
        }).ToArray();
        var postings = lines.Select((line, index) => (line, index))
            .Where(item => states[item.line.ProductId].ManageStock)
            .Select(item =>
            {
                var target = inventoryTargets[item.line.ProductId];
                return new InventoryLedgerPosting(receipt.BusinessId, receipt.WarehouseId,
                    target.ProductId, receipt.DocumentId, PurchasingDocumentTypes.GoodsReceipt,
                    item.line.LineNumber, "GoodsReceipt", item.line.Quantity * target.Factor,
                    costs[item.index].Acquisition / target.Factor,
                    InventoryValuationMode.WeightedAverageReceipt, receipt.ReceivedAt);
            }).ToArray();
        var valuations = await inventoryWriter.PostBatchAsync(session, postings, cancellationToken);
        var byLine = postings.Select((posting, index) => (posting.LineNumber, Result: valuations[index]))
            .ToDictionary(item => item.LineNumber, item => item.Result);
        var previousCosts = states.ToDictionary(item => item.Key, item => item.Value.PreviousObservedUnitCost);
        var effects = new List<ReceiptLineEffects>(lines.Length);
        var families = new List<SqlLinkedProductCostPreparation.Family>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var state = states[line.ProductId];
            var observed = state.ManageStock && state.PriceFormationCostBasis == "WeightedAverageCost"
                ? byLine[line.LineNumber].AverageUnitCostAfter * inventoryTargets[line.ProductId].Factor
                : costs[index].Acquisition;
            ReceiptPriceProposal? proposal = null;
            if (state.CostParentProductId is { } parent)
                families.Add(new(parent, null, line.ProductId, receipt.DocumentId, line.LineNumber));
            else
            {
                if (state.TargetMarginPercent is null or <= 0 or >= 100)
                    throw new InvalidOperationException("The product requires a valid target margin before receiving merchandise.");
                var targetMargin = state.TargetMarginPercent.Value;
                var raw = PriceMargin.CalculateGrossSalePrice(observed, targetMargin, state.SalesTaxRate);
                var rounded = PriceMargin.RoundPrice(raw, state.RoundingIncrement, state.RoundingMode);
                proposal = new(observed, observed <= 0 ? null : PriceMargin.CalculateMarginPercentFromGross(
                        observed, state.CurrentSalePrice, state.SalesTaxRate), targetMargin, raw, rounded,
                    PriceMargin.CalculateMarginPercentFromGross(observed, rounded, state.SalesTaxRate),
                    state.PriceFormationCostBasis == "WeightedAverageCost" ? "WeightedAverageCost" : "LatestReceiptCost",
                    state.RoundingIncrement, state.RoundingMode);
                families.Add(new(line.ProductId, observed, null, receipt.DocumentId, line.LineNumber));
            }
            effects.Add(new(ids.NewId(), line.LineNumber, line.ProductId, costs[index].Supplier,
                previousCosts[line.ProductId], proposal));
            previousCosts[line.ProductId] = costs[index].Supplier;
        }
        var now = timeProvider.GetUtcNow();
        // Resolve linked costs against the pre-receipt pricing state plus the
        // ordered family requests. Later parent lines must not rewrite earlier
        // child preparations; all effects remain in this document transaction.
        await SqlLinkedProductCostPreparation.PrepareFamiliesAsync(session.Connection, session.Transaction,
            receipt.BusinessId, families, receipt.ConfirmedByUserId, now, cancellationToken);
        await PersistLineEffectsAsync(session, receipt.BusinessId, receipt.SupplierId,
            receipt.DocumentId, receipt.ConfirmedByUserId, receipt.ReceivedAt,
            effects, now, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<Guid, ReceiptLineState>> LoadLineStatesAsync(
        SqlDocumentProcessingSessionAccessor.Session session, Guid businessId, Guid warehouseId,
        Guid supplierId,
        IReadOnlyDictionary<Guid, InventoryProductResolution> targets, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            DECLARE @Products TABLE(ProductId uniqueidentifier PRIMARY KEY,InventoryProductId uniqueidentifier);
            INSERT @Products SELECT ProductId,InventoryProductId FROM OPENJSON(@ProductsJson)
              WITH(ProductId uniqueidentifier,InventoryProductId uniqueidentifier);
            SELECT p.ProductId,inventoryProduct.ManageStock,pp.Amount,lc.LatestUnitCost,
                   COALESCE(tax.Rate,0),tenant.InventoryCostBasis,pp.TargetMarginPercent,
                   COALESCE(pp.RoundingIncrement,1),COALESCE(pp.RoundingMode,N'Nearest'),costLink.ParentProductId
            FROM @Products input
            JOIN dbo.Products p WITH(UPDLOCK,HOLDLOCK) ON p.ProductId=input.ProductId
            JOIN dbo.Warehouses w WITH(UPDLOCK,HOLDLOCK) ON w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId
            JOIN dbo.Businesses business WITH(UPDLOCK,HOLDLOCK) ON business.BusinessId=@BusinessId
            JOIN dbo.Tenants tenant WITH(UPDLOCK,HOLDLOCK) ON tenant.TenantId=business.TenantId
            JOIN dbo.SupplierProducts sp WITH(UPDLOCK,HOLDLOCK) ON sp.ProductId=p.ProductId
              AND sp.SupplierId=@SupplierId AND sp.TenantId=business.TenantId AND sp.IsActive=1
            JOIN dbo.Products inventoryProduct WITH(UPDLOCK,HOLDLOCK) ON inventoryProduct.ProductId=input.InventoryProductId
              AND inventoryProduct.TenantId=business.TenantId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=p.TaxProfileId AND tax.TenantId=business.TenantId
            JOIN dbo.ProductPrices pp WITH(UPDLOCK,HOLDLOCK) ON pp.ProductId=p.ProductId
              AND pp.BusinessId=@BusinessId AND pp.IsActive=1
            LEFT JOIN dbo.SupplierProductLatestCosts lc WITH(UPDLOCK,HOLDLOCK) ON lc.BusinessId=@BusinessId
              AND lc.SupplierId=@SupplierId AND lc.ProductId=p.ProductId
            LEFT JOIN dbo.ProductLinks costLink WITH(UPDLOCK,HOLDLOCK) ON costLink.TenantId=business.TenantId
              AND costLink.ChildProductId=p.ProductId AND costLink.SharesPrice=1 AND costLink.IsActive=1
            WHERE p.TenantId=business.TenantId
            ORDER BY p.ProductId OPTION(RECOMPILE);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@SupplierId", supplierId);
        command.Parameters.Add("@ProductsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            targets.Select(item => new { ProductId = item.Key, InventoryProductId = item.Value.ProductId }));
        var states = new Dictionary<Guid, ReceiptLineState>(targets.Count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            states.Add(reader.GetGuid(0), new(reader.GetBoolean(1), reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3), reader.GetDecimal(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6), reader.GetDecimal(7), reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9)));
        if (states.Count != targets.Count)
            throw new InvalidOperationException("The receipt product, price, supplier association or warehouse is no longer valid.");
        return states;
    }

    private static async Task PersistLineEffectsAsync(SqlDocumentProcessingSessionAccessor.Session session,
        Guid businessId, Guid supplierId, Guid sourceDocumentId, Guid userId,
        DateTimeOffset observedAt, IReadOnlyList<ReceiptLineEffects> effects, DateTimeOffset now,
        CancellationToken cancellationToken, int sourceRevision = 0, bool persistSupplierCosts = true,
        bool updateLatestSupplierCosts = false)
    {
        await using var command = new SqlCommand("""
            DECLARE @TenantId uniqueidentifier,@SharesPrices bit;
            SELECT @TenantId=TenantId,@SharesPrices=SharesProductPrices
            FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId;
            DECLARE @Lines TABLE(ObservationId uniqueidentifier,LineNumber int PRIMARY KEY,ProductId uniqueidentifier,
              SupplierCost decimal(19,6),PreviousCost decimal(19,6),HasProposal bit,ObservedCost decimal(19,6),
              CurrentMargin decimal(9,6),TargetMargin decimal(9,6),RawPrice decimal(19,4),RoundedPrice decimal(19,4),
              EffectiveMargin decimal(9,6),CostBasisType nvarchar(32),RoundingIncrement decimal(19,4),RoundingMode nvarchar(16));
            INSERT @Lines SELECT * FROM OPENJSON(@LinesJson) WITH(ObservationId uniqueidentifier,LineNumber int,
              ProductId uniqueidentifier,SupplierCost decimal(19,6),PreviousCost decimal(19,6),HasProposal bit,
              ObservedCost decimal(19,6),CurrentMargin decimal(9,6),TargetMargin decimal(9,6),RawPrice decimal(19,4),
              RoundedPrice decimal(19,4),EffectiveMargin decimal(9,6),CostBasisType nvarchar(32),
              RoundingIncrement decimal(19,4),RoundingMode nvarchar(16));
            DECLARE @Latest TABLE(ProductId uniqueidentifier PRIMARY KEY,LineNumber int);
            INSERT @Latest SELECT ProductId,MAX(LineNumber) FROM @Lines GROUP BY ProductId;
            IF @PersistSupplierCosts=1
            BEGIN
              INSERT dbo.SupplierCostObservations(SupplierCostObservationId,BusinessId,SupplierId,ProductId,
                SourceDocumentId,SourceLineNumber,UnitCost,CurrencyCode,ObservedAt,CreatedAt)
              SELECT ObservationId,@BusinessId,@SupplierId,ProductId,@DocumentId,LineNumber,SupplierCost,N'COP',@ObservedAt,@Now
              FROM @Lines;
              IF @@ROWCOUNT<>@LineCount THROW 51606,'The receipt cost observations were not persisted atomically.',1;
              UPDATE cost SET PreviousUnitCost=line.PreviousCost,LatestUnitCost=line.SupplierCost,CurrencyCode=N'COP',
                SourceDocumentId=@DocumentId,SourceLineNumber=line.LineNumber,ObservedAt=@ObservedAt
              FROM dbo.SupplierProductLatestCosts cost
              JOIN @Latest last ON last.ProductId=cost.ProductId
              JOIN @Lines line ON line.LineNumber=last.LineNumber
              WHERE cost.BusinessId=@BusinessId AND cost.SupplierId=@SupplierId OPTION(RECOMPILE);
              INSERT dbo.SupplierProductLatestCosts(BusinessId,SupplierId,ProductId,PreviousUnitCost,LatestUnitCost,
                CurrencyCode,SourceDocumentId,SourceLineNumber,ObservedAt)
              SELECT @BusinessId,@SupplierId,line.ProductId,line.PreviousCost,line.SupplierCost,N'COP',
                @DocumentId,line.LineNumber,@ObservedAt
              FROM @Latest last JOIN @Lines line ON line.LineNumber=last.LineNumber
              WHERE NOT EXISTS(SELECT 1 FROM dbo.SupplierProductLatestCosts cost WITH(UPDLOCK,HOLDLOCK)
                WHERE cost.BusinessId=@BusinessId AND cost.SupplierId=@SupplierId AND cost.ProductId=line.ProductId)
              OPTION(RECOMPILE);
            END;
            IF @UpdateLatestSupplierCosts=1
            BEGIN
              UPDATE cost SET PreviousUnitCost=cost.LatestUnitCost,
                LatestUnitCost=line.SupplierCost,ObservedAt=@ObservedAt
              FROM dbo.SupplierProductLatestCosts cost
              JOIN @Latest last ON last.ProductId=cost.ProductId
              JOIN @Lines line ON line.LineNumber=last.LineNumber
              WHERE cost.BusinessId=@BusinessId AND cost.SupplierId=@SupplierId
                AND cost.SourceDocumentId=@DocumentId;
            END;
            DECLARE @Changes TABLE(CatalogChangeId bigint);
            INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
              OUTPUT inserted.CatalogChangeId INTO @Changes
            SELECT @BusinessId,last.ProductId,N'Upsert',@Now FROM @Latest last
            WHERE EXISTS(SELECT 1 FROM dbo.ProductPrices price WHERE price.BusinessId=@BusinessId
              AND price.ProductId=last.ProductId AND price.IsActive=1);
            INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            SELECT NEWID(),@BusinessId,N'Catalog',MAX(CatalogChangeId),@Now FROM @Changes HAVING COUNT(*)>0;

            UPDATE proposal SET Status=N'Superseded'
            FROM dbo.PriceRevisionProposals proposal
            JOIN dbo.Businesses target ON target.BusinessId=proposal.BusinessId
            WHERE proposal.Status IN(N'PendingReview',N'Approved')
              AND EXISTS(SELECT 1 FROM @Lines line WHERE line.HasProposal=1 AND line.ProductId=proposal.ProductId)
              AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                OR (@SharesPrices=0 AND proposal.BusinessId=@BusinessId)) OPTION(RECOMPILE);
            DECLARE @Created TABLE(ProposalId uniqueidentifier,BusinessId uniqueidentifier,ProductId uniqueidentifier,
              LineNumber int,Status nvarchar(32),PRIMARY KEY(BusinessId,LineNumber));
            INSERT dbo.PriceRevisionProposals(PriceRevisionProposalId,BusinessId,ProductId,SourceDocumentId,SourceLineNumber,SourceRevision,
              PreviousObservedUnitCost,ObservedUnitCost,CurrentSalePrice,CurrentMarginPercent,TargetMarginPercent,
              SuggestedSalePrice,RoundedSuggestedSalePrice,EffectiveMarginAfterRounding,LastInputMode,Status,CreatedAt)
            OUTPUT inserted.PriceRevisionProposalId,inserted.BusinessId,inserted.ProductId,inserted.SourceLineNumber,
              inserted.Status INTO @Created
            SELECT NEWID(),price.BusinessId,line.ProductId,@DocumentId,line.LineNumber,@SourceRevision,line.PreviousCost,line.ObservedCost,
              price.Amount,line.CurrentMargin,line.TargetMargin,line.RawPrice,line.RoundedPrice,line.EffectiveMargin,N'Margin',
              CASE WHEN line.LineNumber=last.LineNumber THEN N'PendingReview' ELSE N'Superseded' END,@Now
            FROM @Lines line JOIN @Latest last ON last.ProductId=line.ProductId
            JOIN dbo.ProductPrices price ON price.ProductId=line.ProductId AND price.IsActive=1
            JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
            WHERE line.HasProposal=1
              AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                OR (@SharesPrices=0 AND price.BusinessId=@BusinessId)) OPTION(RECOMPILE);
            UPDATE preparation SET Status=N'Superseded',SupersededAt=@Now
            FROM dbo.ProductPricePreparations preparation
            JOIN (SELECT DISTINCT BusinessId,ProductId FROM @Created) created
              ON created.BusinessId=preparation.BusinessId AND created.ProductId=preparation.ProductId
            WHERE preparation.Status=N'Pending';
            INSERT dbo.ProductPricePreparations(ProductPricePreparationId,BusinessId,ProductId,SourceProposalId,
              SourceDocumentId,SourceLineNumber,PreparationOrigin,PublicAmountSnapshot,PreparedAmount,CostBasisType,
              CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,RoundingMode,
              Status,PreparedByUserId,PreparedAt,SupersededAt)
            SELECT NEWID(),created.BusinessId,created.ProductId,created.ProposalId,@DocumentId,line.LineNumber,N'GoodsReceipt',
              price.Amount,line.RoundedPrice,line.CostBasisType,line.ObservedCost,line.TargetMargin,line.EffectiveMargin,N'Margin',
              line.RoundingIncrement,line.RoundingMode,
              CASE WHEN created.Status=N'PendingReview' THEN N'Pending' ELSE N'Superseded' END,@UserId,@Now,
              CASE WHEN created.Status=N'PendingReview' THEN NULL ELSE @Now END
            FROM @Created created JOIN @Lines line ON line.LineNumber=created.LineNumber
            JOIN dbo.ProductPrices price ON price.BusinessId=created.BusinessId AND price.ProductId=created.ProductId AND price.IsActive=1
            OPTION(RECOMPILE);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SupplierId", supplierId);
        command.Parameters.AddWithValue("@DocumentId", sourceDocumentId);
        command.Parameters.AddWithValue("@ObservedAt", observedAt);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@LineCount", effects.Count);
        command.Parameters.AddWithValue("@SourceRevision", sourceRevision);
        command.Parameters.AddWithValue("@PersistSupplierCosts", persistSupplierCosts);
        command.Parameters.AddWithValue("@UpdateLatestSupplierCosts", updateLatestSupplierCosts);
        command.Parameters.Add("@LinesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(effects.Select(line => new
        {
            line.ObservationId, line.LineNumber, line.ProductId, SupplierCost = line.SupplierUnitCost,
            PreviousCost = line.PreviousObservedUnitCost, HasProposal = line.Proposal is not null,
            ObservedCost = line.Proposal?.ObservedCost, CurrentMargin = line.Proposal?.CurrentMargin,
            TargetMargin = line.Proposal?.TargetMargin, RawPrice = line.Proposal?.RawPrice,
            RoundedPrice = line.Proposal?.RoundedPrice, EffectiveMargin = line.Proposal?.EffectiveMargin,
            CostBasisType = line.Proposal?.CostBasisType, RoundingIncrement = line.Proposal?.RoundingIncrement,
            RoundingMode = line.Proposal?.RoundingMode
        }));
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

    private sealed record ReceiptLineEffects(Guid ObservationId,int LineNumber,Guid ProductId,
        decimal SupplierUnitCost,decimal? PreviousObservedUnitCost,ReceiptPriceProposal? Proposal);
    private sealed record ReceiptPriceProposal(decimal ObservedCost,decimal? CurrentMargin,decimal TargetMargin,
        decimal RawPrice,decimal RoundedPrice,decimal? EffectiveMargin,string CostBasisType,
        decimal RoundingIncrement,string RoundingMode);

    private sealed record ReceiptLineState(
        bool ManageStock,
        decimal CurrentSalePrice,
        decimal? PreviousObservedUnitCost,
        decimal SalesTaxRate,
        string PriceFormationCostBasis,
        decimal? TargetMarginPercent,
        decimal RoundingIncrement,
        string RoundingMode,
        Guid? CostParentProductId);
}
