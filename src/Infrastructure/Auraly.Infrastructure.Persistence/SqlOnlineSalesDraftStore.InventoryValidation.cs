using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSalesInventoryValidation> ValidateInventoryAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var validation = await ValidateInventoryAsync(
            connection, transaction, state, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return validation;
    }

    private static async Task<OnlineSalesInventoryValidation> ValidateInventoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DraftState state,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        if (state.WarehouseAllowsNegativeStock)
            return new OnlineSalesInventoryValidation(true, true, []);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @InventoryWarehouseId uniqueidentifier=@WarehouseId;
            IF @SourceOrderId IS NOT NULL
              SELECT @InventoryWarehouseId=CASE
                       WHEN ExternalStatus=N'InventoryReleasedForInvoice'
                         THEN @WarehouseId
                       ELSE OrdersWarehouseId
                     END
              FROM dbo.Orders WITH(UPDLOCK,HOLDLOCK)
              WHERE OrderId=@SourceOrderId AND BusinessId=@BusinessId;

            SELECT line.SalesDraftLineId,line.ProductId,line.ProductCode,line.Description,
                   line.Quantity,COALESCE(link.InventoryFactor,1),
                   COALESCE(link.ParentProductId,line.ProductId) InventoryProductId,
                   inventoryProduct.ManageStock,COALESCE(balance.QuantityOnHand,0),line.Position
            FROM dbo.SalesDraftLines line WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Products product
              ON product.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
             AND product.ProductId=line.ProductId
            LEFT JOIN dbo.ProductLinks link
              ON link.BusinessId=@BusinessId AND link.ChildProductId=line.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            JOIN dbo.Products inventoryProduct
              ON inventoryProduct.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
             AND inventoryProduct.ProductId=COALESCE(link.ParentProductId,line.ProductId)
            LEFT JOIN dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=@BusinessId AND balance.WarehouseId=@InventoryWarehouseId
             AND balance.ProductId=inventoryProduct.ProductId
            WHERE line.SalesDraftId=@DraftId
            ORDER BY line.Position,line.SalesDraftLineId;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", state.BusinessId),
            P("@WarehouseId", state.WarehouseId),
            P("@SourceOrderId", state.SourceOrderId),
            P("@DraftId", draftId)
        ]);

        var lines = new List<InventoryDemandLine>();
        var lineDetails = new Dictionary<Guid, (string Code, string Description)>();
        var availableByInventoryProduct = new Dictionary<Guid, decimal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var lineId = reader.GetGuid(0);
            var inventoryProductId = reader.GetGuid(6);
            lines.Add(new InventoryDemandLine(
                lineId,
                reader.GetGuid(1),
                inventoryProductId,
                reader.GetDecimal(5),
                reader.GetDecimal(4),
                reader.GetBoolean(7)));
            lineDetails[lineId] = (reader.GetString(2), reader.GetString(3));
            availableByInventoryProduct.TryAdd(inventoryProductId, reader.GetDecimal(8));
        }

        var issues = InventoryDemandResolver.AllocateWholeLines(lines, availableByInventoryProduct)
            .Where(allocation => !allocation.CanReserve)
            .Select(allocation =>
            {
                var detail = lineDetails[allocation.Line.LineId];
                return new OnlineSalesInventoryIssue(
                    allocation.Line.LineId,
                    allocation.Line.ProductId,
                    detail.Code,
                    detail.Description,
                    allocation.Line.Quantity,
                    Math.Max(0, decimal.Round(
                        InventoryDemandResolver.InProductUnits(
                            allocation.AvailableInventoryQuantity,
                            allocation.Line.InventoryFactor),
                        6)));
            })
            .ToArray();
        return new OnlineSalesInventoryValidation(issues.Length == 0, true, issues);
    }
}
