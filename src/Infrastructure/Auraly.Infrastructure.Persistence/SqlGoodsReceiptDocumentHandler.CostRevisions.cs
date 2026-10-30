using System.Data;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Inventory;
using Auraly.Domain.Pricing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlGoodsReceiptDocumentHandler
{
    internal async Task HandleCostRevisionAsync(ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var revision = GoodsReceiptContractSerializer.DeserializeCostRevision(document.Payload);
        var edit = revision.CostEdit;
        if (revision.RevisionId != document.DocumentId.Value ||
            revision.BusinessId != document.BusinessId.Value ||
            revision.TenantId != document.TenantId.Value ||
            (edit is null && (revision.ReferencedCostDocuments.Count == 0 ||
            revision.ReferencedCostDocuments.All(item => item.Mode == GoodsReceiptCostAssociationModes.Associate))))
            throw new InvalidOperationException("The cost revision does not match its document envelope.");
        var session = sessions.Current;
        var allocationDelta = edit?.CurrentLines.ToDictionary(line => line.LineNumber,
            line => line.RecognizedInventoryCostAmount -
                edit.PreviousLines.Single(previous => previous.LineNumber == line.LineNumber).RecognizedInventoryCostAmount);
        var affected = edit is null ? revision.ReferencedCostDocuments
            .SelectMany(item => item.Lines)
            .SelectMany(item => item.Allocations)
            .Where(item => item.FunctionalAmount > 0m)
            .Select(item => item.ReceiptLineNumber).Distinct().Order().ToArray()
            : allocationDelta!.Where(item => item.Value != 0m).Select(item => item.Key).Order().ToArray();
        if ((edit is null && affected.Length == 0) || affected.Any(number =>
            (edit?.CurrentLines ?? revision.ReceiptLines).All(line => line.LineNumber != number)))
            throw new InvalidOperationException("The cost revision has no valid merchandise allocation.");

        var origins = affected.Length == 0
            ? new Dictionary<int, RevisionOrigin>()
            : await LoadRevisionOriginsAsync(session, revision, affected, cancellationToken);
        var expectedByLine = revision.ReferencedCostDocuments
            .SelectMany(document => document.Lines)
            .SelectMany(line => line.Allocations)
            .GroupBy(allocation => allocation.ReceiptLineNumber)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.FunctionalAmount));
        if (edit is null && affected.Any(number => origins[number].NewAllocation != expectedByLine[number]))
            throw new InvalidOperationException(
                "La revisión y sus asignaciones aceptadas no concilian; no se ajustó el inventario.");
        var affectedLines = (edit?.CurrentLines ?? revision.ReceiptLines)
            .Where(line => affected.Contains(line.LineNumber))
            .OrderBy(line => line.LineNumber).ToArray();
        var targets = await SqlProductLinkResolution.ResolveInventoryBatchAsync(session,
            revision.BusinessId, affectedLines.Select(line => line.ProductId).ToArray(),
            cancellationToken);
        var states = await LoadLineStatesAsync(session, revision.BusinessId, revision.WarehouseId,
            revision.SupplierId, targets, cancellationToken);
        var capitalizedByProduct = new Dictionary<Guid, decimal>();
        var firstProductLine = new Dictionary<Guid, GoodsReceiptLineSnapshot>();
        foreach (var line in affectedLines)
        {
            var origin = origins[line.LineNumber];
            if (states[line.ProductId].ManageStock)
            {
                if (origin.InventoryProductId is null || !origin.Traceable ||
                    origin.InventoryProductId != targets[line.ProductId].ProductId)
                    throw new InvalidOperationException(
                        $"La línea {line.LineNumber} no conserva el origen de inventario necesario para ajustar su costo con seguridad.");
                if (origin.RemainingQuantity < 0m || origin.RemainingQuantity > origin.ReceivedQuantity ||
                    origin.ReceivedQuantity <= 0m)
                    throw new InvalidOperationException("El origen de la recepción no concilia con sus existencias actuales.");
                var retained = decimal.Round((edit is null
                    ? origin.NewAllocation : allocationDelta![line.LineNumber]) *
                    origin.RemainingQuantity / origin.ReceivedQuantity, 4,
                    MidpointRounding.AwayFromZero);
                if (retained != 0m)
                {
                    var productId = origin.InventoryProductId.Value;
                    capitalizedByProduct[productId] = capitalizedByProduct.GetValueOrDefault(productId) + retained;
                    firstProductLine.TryAdd(productId, line);
                }
            }
        }
        var postings = capitalizedByProduct.Where(item => item.Value != 0m)
            .OrderBy(item => item.Key).Select(item =>
            new InventoryLedgerPosting(revision.BusinessId, revision.WarehouseId,
                firstProductLine[item.Key].ProductId, revision.RevisionId,
                PurchasingDocumentTypes.GoodsReceiptCostRevision,
                firstProductLine[item.Key].LineNumber, "GoodsReceiptCostRevision", 0m, null,
                InventoryValuationMode.ValueAdjustment, revision.OccurredAt,
                ValueAdjustmentAmount: item.Value)).ToArray();
        var valued = await inventoryWriter.PostBatchAsync(session, postings, cancellationToken);
        var valuationByProduct = postings.Select((posting, index) =>
            (ProductId: targets[posting.ProductId].ProductId, Result: valued[index]))
            .ToDictionary(item => item.ProductId, item => item.Result);

        var effects = new List<ReceiptLineEffects>(affectedLines.Length);
        var families = new List<SqlLinkedProductCostPreparation.Family>(affectedLines.Length);
        foreach (var line in affectedLines)
        {
            var state = states[line.ProductId];
            var target = targets[line.ProductId];
            var acquisition = decimal.Round(
                (line.RecognizedInventoryCostAmount + (edit is null
                    ? origins[line.LineNumber].CumulativeAllocation : 0m)) /
                line.Quantity, 6, MidpointRounding.AwayFromZero);
            var hasValuation = valuationByProduct.TryGetValue(target.ProductId, out var valuation);
            var useAverage = state.ManageStock && state.PriceFormationCostBasis == "WeightedAverageCost" &&
                hasValuation;
            // Consumed ingredients keep their production history. With no retained
            // units to revalue, prepare future prices from the revised acquisition.
            var observed = useAverage
                ? valuation!.AverageUnitCostAfter * target.Factor
                : acquisition;
            ReceiptPriceProposal? proposal = null;
            if (state.CostParentProductId is { } parent)
                families.Add(new(parent, null, line.ProductId, revision.GoodsReceiptId, line.LineNumber));
            else
            {
                if (state.TargetMarginPercent is null or <= 0 or >= 100)
                    throw new InvalidOperationException("El producto requiere un margen válido para preparar el precio.");
                var targetMargin = state.TargetMarginPercent.Value;
                var raw = PriceMargin.CalculateGrossSalePrice(observed, targetMargin, state.SalesTaxRate);
                var rounded = PriceMargin.RoundPrice(raw, state.RoundingIncrement, state.RoundingMode);
                proposal = new(observed,
                    observed <= 0 ? null : PriceMargin.CalculateMarginPercentFromGross(
                        observed, state.CurrentSalePrice, state.SalesTaxRate),
                    targetMargin, raw, rounded,
                    PriceMargin.CalculateMarginPercentFromGross(observed, rounded, state.SalesTaxRate),
                    useAverage ? "WeightedAverageCost" : "LatestReceiptCost",
                    state.RoundingIncrement, state.RoundingMode);
                families.Add(new(line.ProductId, observed, null, revision.GoodsReceiptId, line.LineNumber));
            }
            effects.Add(new(ids.NewId(), line.LineNumber, line.ProductId,
                decimal.Round(line.FunctionalNetAmount / line.Quantity, 6, MidpointRounding.AwayFromZero),
                state.PreviousObservedUnitCost, proposal));
        }
        var now = timeProvider.GetUtcNow();
        if (families.Count > 0)
            await SqlLinkedProductCostPreparation.PrepareFamiliesAsync(session.Connection,
                session.Transaction, revision.BusinessId, families,
                revision.AcceptedByUserId, now, cancellationToken);
        if (effects.Count > 0)
            await PersistLineEffectsAsync(session, revision.BusinessId, revision.SupplierId,
                revision.GoodsReceiptId, revision.AcceptedByUserId, revision.OccurredAt,
                effects, now, cancellationToken, revision.RevisionNumber,
                persistSupplierCosts: false, updateLatestSupplierCosts: edit is not null);
        if (edit is not null)
            await PersistCostEditProjectionAsync(session, revision, edit, cancellationToken);
        var newInvoices = revision.NewAdditionalCostDocuments ?? [];
        if (newInvoices.Count > 0)
        {
            await SqlWithholdingRuleStore.InsertDocumentSnapshotsAsync(session.Connection,
                session.Transaction, revision.BusinessId,
                newInvoices.Select(invoice => new SqlWithholdingRuleStore.DocumentSnapshotSource(
                    invoice.CostDocumentId, PurchasingDocumentTypes.GoodsReceiptCostDocument,
                    invoice.IssuedAt, invoice.Withholding)).ToArray(), cancellationToken);
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(session.Connection,
                session.Transaction, newInvoices.Select(invoice =>
                    new SqlAccountingPostingJobWriter.Source(revision.TenantId,
                        revision.BusinessId, invoice.CostDocumentId,
                        PurchasingDocumentTypes.GoodsReceiptCostDocument,
                        GoodsReceiptContractSerializer.SerializeCostDocument(
                            new GoodsReceiptCostDocumentAccountingPayload(revision.TenantId,
                                revision.BusinessId, revision.GoodsReceiptId, invoice)),
                        invoice.IssuedAt, ids.NewId())).ToArray(), now,
                AccountingJobRequirement.PreserveCommercialEffects, cancellationToken);
        }
        await SqlAccountingPostingJobWriter.InsertSourceAsync(session.Connection,
            session.Transaction, revision.TenantId, revision.BusinessId, revision.RevisionId,
            PurchasingDocumentTypes.GoodsReceiptCostRevision, document.Payload,
            revision.OccurredAt, ids, timeProvider, cancellationToken);
        await using var finish = new SqlCommand("""
            UPDATE purchasing.GoodsReceiptCostRevisions SET Status=N'Processed',ProcessedAt=@Now
            WHERE RevisionId=@RevisionId AND GoodsReceiptId=@ReceiptId AND BusinessId=@BusinessId
              AND RevisionNumber=@RevisionNumber AND Status=N'Accepted';
            INSERT dbo.ServerOutboxMessages(MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            VALUES(@MessageId,@RevisionId,N'GoodsReceiptCostRevision',
              N'purchasing.goods-receipt.cost-revised',@Payload,@Now);
            """, session.Connection, session.Transaction);
        finish.Parameters.AddWithValue("@RevisionId", revision.RevisionId);
        finish.Parameters.AddWithValue("@ReceiptId", revision.GoodsReceiptId);
        finish.Parameters.AddWithValue("@BusinessId", revision.BusinessId);
        finish.Parameters.AddWithValue("@RevisionNumber", revision.RevisionNumber);
        finish.Parameters.AddWithValue("@MessageId", ids.NewId());
        finish.Parameters.AddWithValue("@Payload", document.Payload);
        finish.Parameters.AddWithValue("@Now", now);
        if (await finish.ExecuteNonQueryAsync(cancellationToken) != 2)
            throw new DBConcurrencyException("La revisión del costo cambió antes de procesarse.");
    }

    private static async Task<IReadOnlyDictionary<int, RevisionOrigin>> LoadRevisionOriginsAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptCostRevisionPayload revision, IReadOnlyList<int> affected,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            DECLARE @Affected TABLE(LineNumber int PRIMARY KEY);
            INSERT @Affected SELECT value FROM OPENJSON(@LinesJson) WITH(value int '$');
            WITH source AS
            (
              SELECT target.LineNumber,m.InventoryMovementId,m.ProductId,m.QuantityChange
              FROM @Affected target LEFT JOIN dbo.InventoryMovements m WITH(UPDLOCK,HOLDLOCK)
                ON m.DocumentId=@ReceiptId AND m.DocumentType=N'GoodsReceipt'
               AND m.BusinessId=@BusinessId AND m.LineNumber=target.LineNumber
               AND m.MovementType=N'GoodsReceipt'
            ), retained AS
            (
              SELECT position.SourceMovementId,SUM(position.Quantity) Quantity
              FROM inventory.OriginPositions position WITH(UPDLOCK,HOLDLOCK)
              JOIN dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
                ON balance.BusinessId=position.BusinessId
               AND balance.WarehouseId=position.WarehouseId AND balance.ProductId=position.ProductId
               AND balance.OriginContextId=position.ContextId
               AND balance.OriginSnapshotSequence=balance.LastProcessingSequence
              JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=position.WarehouseId
              WHERE position.BusinessId=@BusinessId
                AND NOT(warehouse.IsSystem=1 AND warehouse.Code=N'AVE')
                AND position.SourceMovementId IN
                    (SELECT InventoryMovementId FROM source WHERE InventoryMovementId IS NOT NULL)
              GROUP BY position.SourceMovementId
            ), tracked AS
            (
              SELECT origin.InventoryMovementId FROM inventory.MovementOrigins origin
              WHERE origin.InventoryMovementId IN
                    (SELECT InventoryMovementId FROM source WHERE InventoryMovementId IS NOT NULL)
              GROUP BY origin.InventoryMovementId
            ), allocated AS
            (
              SELECT allocation.ReceiptLineNumber,SUM(allocation.FunctionalAmount) Amount,
                SUM(CASE WHEN cost.CostRevisionId=@RevisionId THEN allocation.FunctionalAmount ELSE 0 END) NewAmount
              FROM purchasing.GoodsReceiptCostAllocations allocation
              JOIN purchasing.GoodsReceiptCostDocuments cost
                ON cost.CostDocumentId=allocation.CostDocumentId
              JOIN purchasing.GoodsReceiptCostRevisions previous
                ON previous.RevisionId=cost.CostRevisionId
               AND previous.RevisionNumber<=@RevisionNumber
               AND previous.GoodsReceiptId=@ReceiptId
              WHERE allocation.GoodsReceiptId=@ReceiptId
                AND allocation.ReceiptLineNumber IN(SELECT LineNumber FROM @Affected)
              GROUP BY allocation.ReceiptLineNumber
            )
            SELECT source.LineNumber,source.ProductId,source.QuantityChange,
              COALESCE(retained.Quantity,0),
              CONVERT(bit,CASE WHEN tracked.InventoryMovementId IS NULL THEN 0 ELSE 1 END),
              COALESCE(allocated.Amount,0),COALESCE(allocated.NewAmount,0)
            FROM source LEFT JOIN retained ON retained.SourceMovementId=source.InventoryMovementId
            LEFT JOIN tracked ON tracked.InventoryMovementId=source.InventoryMovementId
            LEFT JOIN allocated ON allocated.ReceiptLineNumber=source.LineNumber
            ORDER BY source.LineNumber;
            SELECT DISTINCT source.ProductId
            FROM dbo.InventoryMovements source
            JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId
              AND balance.WarehouseId=source.WarehouseId
              AND balance.ProductId=source.ProductId AND balance.QuantityOnHand>0
            WHERE source.BusinessId=@BusinessId AND source.DocumentId=@ReceiptId
              AND source.DocumentType=N'GoodsReceipt'
              AND source.LineNumber IN(SELECT LineNumber FROM @Affected)
              AND (balance.OriginContextId IS NULL OR
                   balance.OriginSnapshotSequence<>balance.LastProcessingSequence);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", revision.BusinessId);
        command.Parameters.AddWithValue("@ReceiptId", revision.GoodsReceiptId);
        command.Parameters.AddWithValue("@RevisionId", revision.RevisionId);
        command.Parameters.AddWithValue("@RevisionNumber", revision.RevisionNumber);
        command.Parameters.Add("@LinesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(affected);
        var result = new Dictionary<int, RevisionOrigin>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetInt32(0), new(
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetBoolean(4),
                reader.GetDecimal(5), reader.GetDecimal(6)));
        await reader.NextResultAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "El inventario de esta compra no conserva el origen necesario para ajustar el costo con seguridad.");
        if (result.Count != affected.Count)
            throw new InvalidOperationException("La revisión no conserva todas sus partidas de compra.");
        return result;
    }

    private sealed record RevisionOrigin(Guid? InventoryProductId,
        decimal ReceivedQuantity, decimal RemainingQuantity, bool Traceable,
        decimal CumulativeAllocation, decimal NewAllocation);

    private static async Task PersistCostEditProjectionAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        GoodsReceiptCostRevisionPayload revision, GoodsReceiptCostEditSnapshot edit,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            DECLARE @Desired TABLE(LineNumber int PRIMARY KEY,UnitCost decimal(19,6),
              DiscountAmount decimal(19,4),CommercialDiscountPercent decimal(9,6),
              NetAmount decimal(19,4),TaxAmount decimal(19,4),LineTotal decimal(19,4),
              FunctionalNetAmount decimal(19,4),FunctionalTaxAmount decimal(19,4),
              FunctionalLineTotal decimal(19,4),AllocatedLandedCostAmount decimal(19,4),
              RecognizedInventoryCostAmount decimal(19,4));
            INSERT @Desired SELECT * FROM OPENJSON(@LinesJson) WITH(
              LineNumber int,UnitCost decimal(19,6),DiscountAmount decimal(19,4),
              CommercialDiscountPercent decimal(9,6),NetAmount decimal(19,4),
              TaxAmount decimal(19,4),LineTotal decimal(19,4),
              FunctionalNetAmount decimal(19,4),FunctionalTaxAmount decimal(19,4),
              FunctionalLineTotal decimal(19,4),AllocatedLandedCostAmount decimal(19,4),
              RecognizedInventoryCostAmount decimal(19,4));
            IF (SELECT COUNT(*) FROM @Desired)<>@LineCount
              THROW 51680,N'La revisión no conserva todas las líneas de costo.',1;
            UPDATE line SET UnitCost=desired.UnitCost,DiscountAmount=desired.DiscountAmount,
              CommercialDiscountPercent=desired.CommercialDiscountPercent,
              NetAmount=desired.NetAmount,TaxAmount=desired.TaxAmount,
              LineTotal=desired.LineTotal,FunctionalNetAmount=desired.FunctionalNetAmount,
              FunctionalTaxAmount=desired.FunctionalTaxAmount,
              FunctionalLineTotal=desired.FunctionalLineTotal,
              AllocatedLandedCostAmount=desired.AllocatedLandedCostAmount,
              RecognizedInventoryCostAmount=desired.RecognizedInventoryCostAmount
            FROM dbo.GoodsReceiptLines line JOIN @Desired desired
              ON desired.LineNumber=line.LineNumber
            WHERE line.GoodsReceiptId=@ReceiptId;
            IF @@ROWCOUNT<>@LineCount
              THROW 51680,N'La recepción cambió antes de aplicar su ajuste de costos.',1;
            UPDATE dbo.GoodsReceipts SET NetAmount=@Net,TaxAmount=@Tax,
              GrandTotal=@Grand,FunctionalNetAmount=@FunctionalNet,
              FunctionalTaxAmount=@FunctionalTax,FunctionalGrandTotal=@FunctionalGrand
            WHERE GoodsReceiptId=@ReceiptId AND BusinessId=@BusinessId
              AND Status=N'Processed';
            IF @@ROWCOUNT<>1
              THROW 51680,N'La recepción no está disponible para ajustar costos.',1;
            """, session.Connection, session.Transaction);
        var lines = edit.CurrentLines;
        command.Parameters.Add("@LinesJson", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(lines);
        command.Parameters.AddWithValue("@LineCount", lines.Count);
        command.Parameters.AddWithValue("@ReceiptId", revision.GoodsReceiptId);
        command.Parameters.AddWithValue("@BusinessId", revision.BusinessId);
        command.Parameters.AddWithValue("@Net", lines.Sum(line => line.NetAmount));
        command.Parameters.AddWithValue("@Tax", lines.Sum(line => line.TaxAmount));
        command.Parameters.AddWithValue("@Grand", lines.Sum(line => line.LineTotal));
        command.Parameters.AddWithValue("@FunctionalNet", lines.Sum(line => line.FunctionalNetAmount));
        command.Parameters.AddWithValue("@FunctionalTax", lines.Sum(line => line.FunctionalTaxAmount));
        command.Parameters.AddWithValue("@FunctionalGrand", edit.CurrentFunctionalGrandTotal);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SqlGoodsReceiptCostRevisionDocumentHandler(
    SqlGoodsReceiptDocumentHandler receipts) : IConfirmedDocumentHandler
{
    public string DocumentType => PurchasingDocumentTypes.GoodsReceiptCostRevision;
    public Task HandleAsync(ConfirmedDocument document, CancellationToken cancellationToken) =>
        receipts.HandleCostRevisionAsync(document, cancellationToken);
}
