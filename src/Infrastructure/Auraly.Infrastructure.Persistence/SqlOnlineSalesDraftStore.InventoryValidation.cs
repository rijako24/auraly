using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
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
            SELECT line.SalesDraftLineId,line.ProductId,line.ProductCode,line.Description,
                   line.Quantity,COALESCE(link.InventoryFactor,1),
                   COALESCE(link.ParentProductId,line.ProductId) InventoryProductId,
                   inventoryProduct.ManageStock,COALESCE(balance.QuantityOnHand,0),line.Position
            FROM dbo.SalesDraftLines line WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Products product
              ON product.BusinessId=@BusinessId AND product.ProductId=line.ProductId
            LEFT JOIN dbo.ProductLinks link
              ON link.BusinessId=@BusinessId AND link.ChildProductId=line.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            JOIN dbo.Products inventoryProduct
              ON inventoryProduct.BusinessId=@BusinessId
             AND inventoryProduct.ProductId=COALESCE(link.ParentProductId,line.ProductId)
            LEFT JOIN dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId
             AND balance.ProductId=inventoryProduct.ProductId
            WHERE line.SalesDraftId=@DraftId
            ORDER BY line.Position,line.SalesDraftLineId;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", state.BusinessId),
            P("@WarehouseId", state.WarehouseId),
            P("@DraftId", draftId)
        ]);

        var remaining = new Dictionary<Guid, decimal>();
        var issues = new List<OnlineSalesInventoryIssue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.GetBoolean(7)) continue;
            var inventoryProductId = reader.GetGuid(6);
            var factor = reader.GetDecimal(5);
            var available = remaining.TryGetValue(inventoryProductId, out var current)
                ? current
                : reader.GetDecimal(8);
            var requested = reader.GetDecimal(4);
            var requiredInventory = requested * factor;
            if (requiredInventory > available)
            {
                issues.Add(new OnlineSalesInventoryIssue(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), requested,
                    Math.Max(0, decimal.Round(available / factor, 6))));
            }
            remaining[inventoryProductId] = Math.Max(0, available - requiredInventory);
        }
        return new OnlineSalesInventoryValidation(issues.Count == 0, true, issues);
    }
}
