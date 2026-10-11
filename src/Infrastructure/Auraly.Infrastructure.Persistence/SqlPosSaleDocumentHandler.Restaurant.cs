using System.Data;
using System.Text.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleDocumentHandler
{
    private sealed record RestaurantRecipeComponent(
        int SaleLineNumber, int MovementLineNumber,
        Guid IngredientProductId, decimal Quantity);

    private sealed record RestaurantInventoryPlan(
        IReadOnlyList<RestaurantRecipeComponent> Components);

    private static async Task<RestaurantInventoryPlan?> ReadRestaurantInventoryPlanAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT mapping.LineNumber,mapping.ProductRecipeVersionId,
                   ingredient.IngredientProductId,ingredient.QuantityPerUnit,
                   recipe.ProductId,finished.ManageStock,
                   addition.ProductId,component.ManageStock,
                   addition.ManageStockAtSend,soldProduct.ManageStock
            FROM sales.RestaurantCheckoutLines mapping
            JOIN sales.RestaurantAccountAdditions addition
              ON addition.AccountId=mapping.AccountId
             AND addition.AdditionId=mapping.AdditionId
            JOIN dbo.Products soldProduct
              ON soldProduct.TenantId=addition.TenantId
             AND soldProduct.ProductId=addition.ProductId
            LEFT JOIN dbo.ProductRecipeVersions recipe
              ON recipe.ProductRecipeVersionId=mapping.ProductRecipeVersionId
             AND recipe.TenantId=mapping.TenantId
            LEFT JOIN dbo.Products finished
              ON finished.ProductId=recipe.ProductId
             AND finished.TenantId=recipe.TenantId
            LEFT JOIN dbo.ProductRecipeIngredients ingredient
              ON ingredient.ProductRecipeVersionId=recipe.ProductRecipeVersionId
             AND ingredient.TenantId=recipe.TenantId
            LEFT JOIN dbo.Products component
              ON component.ProductId=ingredient.IngredientProductId
             AND component.TenantId=ingredient.TenantId
            WHERE mapping.DocumentId=@DocumentId
              AND mapping.BusinessId=@BusinessId
              AND mapping.TenantId=@TenantId
            ORDER BY mapping.LineNumber,ingredient.LineNumber;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        var sourceLines = request.Lines.ToDictionary(line => line.LineNumber);
        var seen = new HashSet<int>();
        var recipeLines = new HashSet<int>();
        var components = new List<RestaurantRecipeComponent>();
        var nextMovementLine = request.Lines.Max(line => line.LineNumber);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var lineNumber = reader.GetInt32(0);
            if (!sourceLines.TryGetValue(lineNumber, out var line) ||
                line.ProductId != reader.GetGuid(6))
                throw new InvalidOperationException(
                    "La factura no coincide con la cuenta de restaurante preparada.");
            if (reader.IsDBNull(8) || reader.GetBoolean(8) != reader.GetBoolean(9))
                throw new InvalidOperationException(
                    "Cambió la modalidad de inventario de un producto enviado. Restablécela para procesar la factura.");
            seen.Add(lineNumber);
            if (reader.IsDBNull(1)) continue;
            recipeLines.Add(lineNumber);
            if (reader.IsDBNull(2) || reader.IsDBNull(3) ||
                reader.IsDBNull(4) || reader.IsDBNull(5) ||
                reader.IsDBNull(7) || reader.GetBoolean(5) ||
                !reader.GetBoolean(7) || reader.GetGuid(4) != line.ProductId)
                throw new InvalidOperationException(
                    "La receta congelada de la cuenta no puede descontarse con seguridad.");
            components.Add(new(lineNumber, checked(++nextMovementLine),
                reader.GetGuid(2), decimal.Round(
                    line.Quantity * reader.GetDecimal(3), 6,
                    MidpointRounding.AwayFromZero)));
        }
        if (seen.Count == 0) return null;
        if (seen.Count != request.Lines.Count ||
            recipeLines.Any(line => !components.Any(component =>
                component.SaleLineNumber == line)) ||
            components.Any(component => component.Quantity <= 0m))
            throw new InvalidOperationException(
                "La receta o el detalle de la cuenta está incompleto.");
        return new(components);
    }

    private static async Task PersistRestaurantInventoryCostsAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        IReadOnlyList<InventoryLedgerPosting> postings,
        IReadOnlyList<InventoryLedgerPostingResult> results,
        RestaurantInventoryPlan plan, CancellationToken ct)
    {
        var ownerByMovement = plan.Components.ToDictionary(
            component => component.MovementLineNumber,
            component => component.SaleLineNumber);
        var costByLine = request.Lines.ToDictionary(line => line.LineNumber,
            _ => 0m);
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
        var lineCosts = request.Lines.Select(line => new {
            line.LineNumber,
            Cost = costByLine[line.LineNumber],
            UnitCost = decimal.Round(costByLine[line.LineNumber] /
                line.Quantity, 6, MidpointRounding.AwayFromZero)
        }).ToArray();
        await using var command = new SqlCommand("""
            UPDATE mapping
            SET RecognizedCostAmount=cost.Cost
            FROM sales.RestaurantCheckoutLines mapping
            JOIN OPENJSON(@LineCosts) WITH
              (LineNumber int,Cost decimal(19,4),UnitCost decimal(19,6)) cost
              ON cost.LineNumber=mapping.LineNumber
            WHERE mapping.DocumentId=@DocumentId;

            UPDATE line
            SET UnitCostSnapshot=cost.UnitCost
            FROM dbo.SalesDocumentLines line
            JOIN OPENJSON(@LineCosts) WITH
              (LineNumber int,Cost decimal(19,4),UnitCost decimal(19,6)) cost
              ON cost.LineNumber=line.LineNumber
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
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.Add("@LineCosts", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(lineCosts);
        command.Parameters.Add("@Components", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(componentCosts);
        await command.ExecuteNonQueryAsync(ct);
    }
}
