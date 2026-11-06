using System.Data;
using System.Text.Json;
using Auraly.Contracts.Sales;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleDocumentHandler
{
    private sealed record SaleCompositionComponent(
        int SaleLineNumber, int MovementLineNumber,
        Guid IngredientProductId, decimal Quantity);

    private sealed record SaleInventoryPlan(
        IReadOnlyList<InventoryLedgerPosting> Postings,
        IReadOnlyList<SaleCompositionComponent> Components,
        IReadOnlyDictionary<int, Guid?> CompositionVersions);

    private static async Task<SaleInventoryPlan> ReadSaleInventoryPlanAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request, Guid warehouseId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @Frozen nvarchar(max);
            SELECT @Frozen=CASE WHEN payload.ContractVersion<2 THEN
                (SELECT frozen.* FROM inventory.SaleCompositionSnapshot(@TenantId,@DocumentId,payload.PayloadJson) frozen
                 ORDER BY frozen.LineNumber FOR JSON PATH,INCLUDE_NULL_VALUES)
                ELSE payload.InventoryCompositionJson END
            FROM dbo.DocumentProcessingPayloads payload
            WHERE payload.DocumentId=@DocumentId AND payload.DocumentType=@DocumentType
              AND payload.BusinessId=@BusinessId;
            SELECT input.LineNumber,product.ProductId,
                   frozen.ManageStock,product.ManageStock,frozen.ProductRecipeVersionId,
                   consumption.InventoryProductId,consumption.InventoryFactor,
                   consumption.ManageStock,consumption.IsComponent,
                   recipe.ProductId,frozen.LineNumber
            FROM OPENJSON(@PayloadJson,'$.lines') WITH
              (LineNumber int '$.lineNumber',ProductId uniqueidentifier '$.productId') input
            JOIN dbo.Products product
              ON product.TenantId=@TenantId AND product.ProductId=input.ProductId
            LEFT JOIN OPENJSON(COALESCE(@Frozen,N'[]')) WITH
              (LineNumber int,ManageStock bit,ProductRecipeVersionId uniqueidentifier) frozen
              ON frozen.LineNumber=input.LineNumber
            LEFT JOIN catalog.ProductCompositionVersions recipe
              ON recipe.TenantId=@TenantId AND recipe.ProductRecipeVersionId=frozen.ProductRecipeVersionId
            OUTER APPLY inventory.ProductConsumption(@TenantId,input.ProductId,
                COALESCE(frozen.ManageStock,product.ManageStock),frozen.ProductRecipeVersionId) consumption
            ORDER BY input.LineNumber,consumption.ComponentLineNumber,consumption.InventoryProductId;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", request.CommercialSnapshot.DocumentType);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.Add("@PayloadJson", SqlDbType.NVarChar, -1).Value = PosSaleContractSerializer.Serialize(request);
        var sourceLines = request.Lines.ToDictionary(line => line.LineNumber);
        var seen = new HashSet<int>();
        var components = new List<SaleCompositionComponent>();
        var versions = new Dictionary<int, Guid?>();
        var postings = new List<InventoryLedgerPosting>();
        var nextMovementLine = request.Lines.Max(line => line.LineNumber);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var lineNumber = reader.GetInt32(0);
            if (!sourceLines.TryGetValue(lineNumber, out var line) || line.ProductId != reader.GetGuid(1))
                throw new InvalidOperationException("La composición no coincide con el producto de la venta.");
            seen.Add(lineNumber);
            var versionId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
            versions[lineNumber] = versionId;
            if (line.IsGenericProductSnapshot) continue;
            if (!reader.IsDBNull(10) && reader.GetBoolean(2) != reader.GetBoolean(3))
                throw new InvalidOperationException("Cambió la modalidad de inventario de una venta aceptada.");
            if (versionId is not null && (reader.IsDBNull(9) || reader.GetGuid(9) != line.ProductId))
                throw new InvalidOperationException("La versión de composición no pertenece al producto vendido.");
            if ((!reader.IsDBNull(10) && !reader.GetBoolean(2) && versionId is null) || reader.IsDBNull(5))
                throw new InvalidOperationException("La venta no tiene una composición válida para consumir inventario.");
            var isComponent = reader.GetBoolean(8);
            if (!reader.GetBoolean(7))
            {
                if (isComponent) throw new InvalidOperationException("Un componente aceptado dejó de manejar inventario.");
                continue;
            }
            var productId = reader.GetGuid(5);
            var demand = new InventoryDemandLine(Guid.Empty, line.ProductId, productId,
                reader.GetDecimal(6), line.Quantity, true);
            var quantity = decimal.Round(demand.RequiredInventoryQuantity, 6, MidpointRounding.AwayFromZero);
            if (quantity <= 0m || quantity > 9999999999999.999999m)
                throw new InvalidOperationException("La cantidad de consumo de inventario no es válida.");
            var movementLine = isComponent ? checked(++nextMovementLine) : lineNumber;
            postings.Add(new(request.BusinessId,isComponent ? request.WarehouseId : warehouseId,productId,request.DocumentId,
                request.CommercialSnapshot.DocumentType,movementLine,"Sale",-quantity,null,
                InventoryValuationMode.AverageCost,request.CommercialSnapshot.IssuedAt));
            if (isComponent) components.Add(new(lineNumber,movementLine,productId,quantity));
        }
        if (seen.Count != request.Lines.Count)
            throw new InvalidOperationException("Faltan productos de la venta en el alcance del inventario.");
        return new(postings,components,versions);
    }

    private static async Task InsertSaleLinesAndCostsAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        IReadOnlyList<InventoryLedgerPosting> postings,
        IReadOnlyList<InventoryLedgerPostingResult> results,
        SaleInventoryPlan plan, CancellationToken ct)
    {
        var ownerByMovement = plan.Components.ToDictionary(
            component => component.MovementLineNumber,
            component => component.SaleLineNumber);
        var costByLine = request.Lines.ToDictionary(line => line.LineNumber,
            line => line.IsGenericProductSnapshot
                ? decimal.Round(line.DocumentUnitCost * line.Quantity,4,MidpointRounding.AwayFromZero) : 0m);
        var componentCosts = new List<object>(plan.Components.Count);
        for (var i = 0; i < postings.Count; i++)
        {
            var posting = postings[i];
            var cost = decimal.Round(-results[i].ValueChange, 4,
                MidpointRounding.AwayFromZero);
            if (cost < 0m)
                throw new InvalidOperationException(
                    "El costo reconocido de la venta es negativo.");
            var saleLine = ownerByMovement.GetValueOrDefault(
                posting.LineNumber, posting.LineNumber);
            costByLine[saleLine] += cost;
            if (ownerByMovement.ContainsKey(posting.LineNumber))
                componentCosts.Add(new {
                    MovementLineNumber = posting.LineNumber,
                    SaleLineNumber = saleLine,
                    posting.ProductId,
                    Quantity = -posting.QuantityChange,
                    Cost = cost
                });
        }
        var movementLines = postings.Select(posting => posting.LineNumber).ToHashSet();
        var lineCosts = request.Lines.Select(line => new {
            line.LineNumber,
            ProductRecipeVersionId = plan.CompositionVersions[line.LineNumber],
            UpdateUnitCost = plan.CompositionVersions[line.LineNumber] is not null ||
                movementLines.Contains(line.LineNumber) || line.IsGenericProductSnapshot || request.TableId is not null,
            Cost = costByLine[line.LineNumber],
            UnitCost = decimal.Round(costByLine[line.LineNumber] /
                line.Quantity, 6, MidpointRounding.AwayFromZero)
        }).ToArray();
        await using var command = CreateInsertLinesCommand(session, request);
        command.CommandText += """

            UPDATE mapping
            SET RecognizedCostAmount=cost.Cost
            FROM sales.RestaurantCheckoutLines mapping
            JOIN OPENJSON(@LineCosts) WITH
              (LineNumber int,Cost decimal(19,4),UnitCost decimal(19,6)) cost
              ON cost.LineNumber=mapping.LineNumber
            WHERE mapping.DocumentId=@DocumentId;

            UPDATE line
            SET UnitCostSnapshot=CASE WHEN cost.UpdateUnitCost=1 THEN cost.UnitCost ELSE line.UnitCostSnapshot END,
                RecognizedCostAmount=CASE WHEN cost.UpdateUnitCost=1 THEN cost.Cost ELSE line.RecognizedCostAmount END,
                ProductRecipeVersionId=cost.ProductRecipeVersionId,
                CompositionKindSnapshot=COALESCE(recipe.CompositionKind,N'None')
            FROM dbo.SalesDocumentLines line
            JOIN OPENJSON(@LineCosts) WITH
              (LineNumber int,Cost decimal(19,4),UnitCost decimal(19,6),ProductRecipeVersionId uniqueidentifier,UpdateUnitCost bit) cost
              ON cost.LineNumber=line.LineNumber
            LEFT JOIN catalog.ProductCompositionVersions recipe ON recipe.ProductRecipeVersionId=cost.ProductRecipeVersionId
            WHERE line.DocumentId=@DocumentId;

            INSERT sales.RestaurantRecipeConsumptionLines
              (DocumentId,SaleLineNumber,MovementLineNumber,
               IngredientProductId,Quantity,RecognizedCostAmount)
            SELECT @DocumentId,component.SaleLineNumber,
                   component.MovementLineNumber,component.ProductId,
                   component.Quantity,component.Cost
            FROM OPENJSON(@Components) WITH
              (SaleLineNumber int,MovementLineNumber int,
               ProductId uniqueidentifier,Quantity decimal(19,6),
               Cost decimal(19,4)) component;
            """;
        command.Parameters.Add("@LineCosts", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(lineCosts);
        command.Parameters.Add("@Components", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(componentCosts);
        await command.ExecuteNonQueryAsync(ct);
    }
}
