using Auraly.Contracts.Inventory;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    private async Task ReleaseOrderInventoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        DraftState state,
        CancellationToken cancellationToken)
    {
        if (state.SourceOrderId is null) return;

        const string orderSql = """
            SELECT OrdersWarehouseId,ReleaseTransferId,ExternalStatus,OrderNumber
            FROM dbo.Orders WITH(UPDLOCK,HOLDLOCK)
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
            """;
        Guid ordersWarehouseId; Guid? existingTransferId; string? externalStatus; string orderNumber;
        await using (var command = new SqlCommand(orderSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@OrderId", state.SourceOrderId.Value);
            command.Parameters.AddWithValue("@BusinessId", state.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new OnlineSalesDraftValidationException("El pedido de origen no existe en este negocio.");
            if (reader.IsDBNull(0))
                throw new OnlineSalesDraftValidationException("El pedido no tiene una bodega de pedidos asociada.");
            ordersWarehouseId = reader.GetGuid(0);
            existingTransferId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            externalStatus = reader.IsDBNull(2) ? null : reader.GetString(2);
            orderNumber = reader.GetString(3);
        }
        if (externalStatus == "InventoryReleasedForInvoice") return;
        if (existingTransferId is not null)
            throw new OnlineSalesDraftConcurrencyException("El traslado de salida del pedido quedó en un estado inconsistente.");

        const string productsSql = """
            SELECT item.ProductId,SUM(item.Quantity)
            FROM dbo.OrderItems item WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Products product WITH(UPDLOCK,HOLDLOCK)
              ON product.ProductId=item.ProductId AND product.BusinessId=item.BusinessId
            WHERE item.OrderId=@OrderId AND item.BusinessId=@BusinessId AND product.ManageStock=1
            GROUP BY item.ProductId
            ORDER BY item.ProductId;
            """;
        var inventoryLines = new List<(Guid ProductId, decimal Quantity)>();
        await using (var command = new SqlCommand(productsSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@BusinessId", state.BusinessId);
            command.Parameters.AddWithValue("@OrderId", state.SourceOrderId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                inventoryLines.Add((reader.GetGuid(0), reader.GetDecimal(1)));
        }
        var lines = inventoryLines
            .Select((line, index) => new WarehouseTransferLineRequest(index + 1, line.ProductId, line.Quantity))
            .ToArray();
        if (lines.Length == 0)
        {
            await MarkOrderInventoryReleasedAsync(connection, transaction, state.SourceOrderId.Value, state.BusinessId,
                null, cancellationToken);
            return;
        }

        var transferId = ids.NewId();
        var identity = new InventoryUserIdentity(user.UserId, user.TenantId, state.BusinessId,
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryPermissionCodes.DispatchTransfer,
                "inventory.system-warehouses.use"
            });
        await inventoryOperations.ConfirmSystemTransferAtomicallyAsync(identity,
            $"seller-order-release:{state.SourceOrderId.Value:N}",
            new DispatchWarehouseTransferRequest(transferId, state.BusinessId, ordersWarehouseId,
                state.WarehouseId, time.GetUtcNow(), "WAREHOUSE_TRANSFER",
                $"Salida completa del pedido {orderNumber} para facturación", lines),
            connection, transaction, cancellationToken);
        await MarkOrderInventoryReleasedAsync(connection, transaction, state.SourceOrderId.Value, state.BusinessId,
            transferId, cancellationToken);
    }

    private static async Task MarkOrderInventoryReleasedAsync(
        SqlConnection connection, SqlTransaction transaction, Guid orderId, Guid businessId,
        Guid? transferId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.Orders
            SET ReleaseTransferId=@TransferId,ExternalStatus=N'InventoryReleasedForInvoice',UpdatedAt=SYSUTCDATETIME()
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId AND ReleaseTransferId IS NULL;
            IF @@ROWCOUNT<>1 THROW 51240,'The order inventory release could not be recorded.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TransferId", (object?)transferId ?? DBNull.Value);
        command.Parameters.AddWithValue("@OrderId", orderId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
