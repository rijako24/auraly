using System.Data;
using Auraly.Application.Inventory;
using Auraly.Application.Orders;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Orders;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

/// <summary>
/// Releases seller-order stock from the PED warehouse back to the selected sales
/// warehouse. Both legs are accepted documents; only the document engine moves stock.
/// </summary>
public sealed class SellerOrderInvoiceInventoryService(
    SqlServerConnectionFactory connections,
    InventoryOperationService inventory)
{
    public async Task PrepareAsync(
        OrderActor actor,
        InvoiceOrdersRequest request,
        CancellationToken token)
    {
        foreach (var orderId in request.OrderIds.Distinct())
        {
            var reservation = await LoadReservationAsync(actor, orderId, token);
            if (reservation is null || reservation.StockLines.Count == 0)
                continue;
            if (reservation.OrdersWarehouseId == request.WarehouseId)
                continue;

            await WaitForDocumentAsync(
                reservation.ReservationTransferId,
                InventoryDocumentTypes.Transfer,
                "La reserva del pedido todavía se está procesando.",
                token);

            var releaseId = SellerOrderWriter.DeterministicDocumentId(
                $"seller-order-release:{orderId:N}:{request.WarehouseId:N}");
            var identity = new InventoryUserIdentity(
                actor.UserId,
                actor.TenantId,
                actor.BusinessId,
                new HashSet<string>(actor.Permissions, StringComparer.Ordinal)
                {
                    InventoryPermissionCodes.Transfer
                });
            await inventory.ConfirmTransferAsync(
                identity,
                $"seller-order-release:{orderId:N}:{request.WarehouseId:N}",
                new ConfirmWarehouseTransferRequest(
                    releaseId,
                    actor.BusinessId,
                    reservation.OrdersWarehouseId,
                    request.WarehouseId,
                    DateTimeOffset.UtcNow,
                    "WAREHOUSE_TRANSFER",
                    $"Retorno a bodega de venta para facturar {reservation.OrderNumber}",
                    reservation.StockLines),
                token);

            await WaitForDocumentAsync(
                releaseId,
                InventoryDocumentTypes.Transfer,
                "El retorno del pedido a la bodega de venta todavía se está procesando.",
                token);
            await MarkReleasedAsync(actor, orderId, request.WarehouseId, token);
        }
    }

    private async Task<Reservation?> LoadReservationAsync(
        OrderActor actor,
        Guid orderId,
        CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        const string headerSql = """
            SELECT ExternalDocumentNumber,
                   TRY_CONVERT(uniqueidentifier,JSON_VALUE(CustomAttributesJson,'$.reservationTransferId')),
                   TRY_CONVERT(uniqueidentifier,JSON_VALUE(CustomAttributesJson,'$.ordersWarehouseId')),
                   Status
            FROM dbo.Orders
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId
              AND ISJSON(CustomAttributesJson)=1;
            """;
        Guid reservationId;
        Guid ordersWarehouseId;
        string orderNumber;
        await using (var header = new SqlCommand(headerSql, connection))
        {
            header.Parameters.AddWithValue("@OrderId", orderId);
            header.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
            await using var reader = await header.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) || reader.IsDBNull(1) || reader.IsDBNull(2))
                return null;
            orderNumber = reader.IsDBNull(0) ? orderId.ToString("D") : reader.GetString(0);
            reservationId = reader.GetGuid(1);
            ordersWarehouseId = reader.GetGuid(2);
            if (reader.GetInt32(3) == 5)
                throw new OrderConflictException(
                    $"El pedido {orderNumber} requiere revisión de existencias antes de facturarse.");
        }

        const string linesSql = """
            SELECT item.ProductId,item.Quantity
            FROM dbo.OrderItems item
            INNER JOIN dbo.Products product ON product.ProductId=item.ProductId
            WHERE item.OrderId=@OrderId AND item.BusinessId=@BusinessId AND product.ManageStock=1
            ORDER BY item.CreatedAt,item.OrderItemId;
            """;
        var lines = new List<WarehouseTransferLineRequest>();
        await using (var command = new SqlCommand(linesSql, connection))
        {
            command.Parameters.AddWithValue("@OrderId", orderId);
            command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                lines.Add(new(lines.Count + 1, reader.GetGuid(0), reader.GetDecimal(1)));
        }
        return new(orderNumber, reservationId, ordersWarehouseId, lines);
    }

    private async Task WaitForDocumentAsync(
        Guid documentId,
        string documentType,
        string pendingMessage,
        CancellationToken token)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < until)
        {
            await using var connection = connections.Create();
            await connection.OpenAsync(token);
            await using var command = new SqlCommand(
                "SELECT Status,LastError FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id AND DocumentType=@Type",
                connection);
            command.Parameters.AddWithValue("@Id", documentId);
            command.Parameters.AddWithValue("@Type", documentType);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                var status = reader.GetString(0);
                if (status == "Completed") return;
                if (status is "NeedsIntervention" or "DeadLettered")
                    throw new OrderConflictException(
                        reader.IsDBNull(1) ? "El traslado de inventario requiere intervención." : reader.GetString(1));
            }
            await Task.Delay(300, token);
        }
        throw new OrderConflictException(pendingMessage + " Intenta nuevamente en unos segundos.");
    }

    private async Task MarkReleasedAsync(
        OrderActor actor,
        Guid orderId,
        Guid warehouseId,
        CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            UPDATE dbo.Orders
            SET ExternalStatus=N'InventoryReleasedForInvoice',
                CustomAttributesJson=JSON_MODIFY(CustomAttributesJson,'$.invoiceWarehouseId',CONVERT(nvarchar(36),@WarehouseId)),
                UpdatedAt=SYSUTCDATETIME()
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
            """, connection);
        command.Parameters.AddWithValue("@OrderId", orderId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        await command.ExecuteNonQueryAsync(token);
    }

    private sealed record Reservation(
        string OrderNumber,
        Guid ReservationTransferId,
        Guid OrdersWarehouseId,
        IReadOnlyList<WarehouseTransferLineRequest> StockLines);
}
