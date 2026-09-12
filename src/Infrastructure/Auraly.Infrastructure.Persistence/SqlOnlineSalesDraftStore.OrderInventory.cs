using Auraly.Contracts.Inventory;
using Auraly.Application.Inventory;
using Auraly.Application.Sales;
using Auraly.Application.Orders;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<StoredOrderCancellation> CancelAsync(
        OrderActor actor,
        Guid orderId,
        Guid? workSessionId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await CancelOrderCoreAsync(
                connection,
                transaction,
                actor,
                orderId,
                workSessionId,
                reason,
                idempotencyKey,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<StoredOrderCancellation> CancelOrderCoreAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OrderActor actor,
        Guid orderId,
        Guid? workSessionId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string orderSql = """
            SELECT o.Status,o.WarehouseId,
                   COALESCE(NULLIF(o.ExternalDocumentNumber,N''),
                            CONCAT(N'PED-',LEFT(CONVERT(nvarchar(36),o.OrderId),8))),
                   CASE WHEN link.OrderId IS NULL THEN 0 ELSE 1 END,
                   claim.WorkSessionId,claim.UserId
            FROM dbo.Orders o WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses business
              ON business.BusinessId=o.BusinessId AND business.TenantId=@TenantId
            LEFT JOIN dbo.OrderInvoiceLinks link ON link.OrderId=o.OrderId
            OUTER APPLY (
              SELECT TOP(1) active.WorkSessionId,active.UserId
              FROM dbo.OrderClaims active WITH(UPDLOCK,HOLDLOCK)
              WHERE active.OrderId=o.OrderId
                AND active.ReleasedAt IS NULL
                AND active.ExpiresAt>@Now
              ORDER BY active.ClaimedAt DESC
            ) claim
            WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId;
            """;
        int status;
        Guid? warehouseId;
        string orderNumber;
        bool hasInvoice;
        Guid? claimedWorkSessionId;
        Guid? claimedUserId;
        await using (var command = new SqlCommand(orderSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@TenantId", actor.TenantId);
            command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
            command.Parameters.AddWithValue("@OrderId", orderId);
            command.Parameters.AddWithValue("@Now", time.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new OrderNotFoundException("El pedido no existe en esta sede.");
            status = reader.GetInt32(0);
            warehouseId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            orderNumber = reader.GetString(2);
            hasInvoice = reader.GetInt32(3) == 1;
            claimedWorkSessionId = reader.IsDBNull(4) ? null : reader.GetGuid(4);
            claimedUserId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
        }

        if (status == 6)
        {
            var replayVersion = await orderReportingJobs.EnsureAsync(
                connection,
                transaction,
                actor.TenantId,
                actor.BusinessId,
                orderId,
                cancellationToken);
            return new(orderId, orderNumber, replayVersion, true);
        }
        if (status is not (2 or 4 or 5) || hasInvoice)
            throw new OrderConflictException(
                "Solo se puede eliminar un pedido disponible o en revisión que todavía no haya sido facturado.");
        if (warehouseId is null)
            throw new OrderConflictException(
                "El pedido no tiene una bodega de venta asignada.");
        if (claimedWorkSessionId is not null &&
            (workSessionId != claimedWorkSessionId || claimedUserId != actor.UserId))
            throw new OrderConflictException(
                "El pedido está siendo usado en otra venta y no se puede eliminar.");

        await ReleaseOrderInventoryCoreAsync(
            connection,
            transaction,
            new OnlineSalesUserIdentity(actor.UserId, actor.TenantId, actor.Permissions),
            orderId,
            actor.BusinessId,
            warehouseId.Value,
            cancellationToken);

        var now = time.GetUtcNow();
        await using (var command = new SqlCommand("""
            UPDATE dbo.Orders
            SET Status=6,RequiresStockReview=0,ExternalStatus=N'Cancelled',UpdatedAt=@Now
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId AND Status IN(2,4,5);
            IF @@ROWCOUNT<>1 THROW 51241,'The order could not be cancelled.',1;

            UPDATE dbo.OrderClaims
            SET ReleasedAt=COALESCE(ReleasedAt,@Now)
            WHERE OrderId=@OrderId AND ReleasedAt IS NULL;

            INSERT dbo.AuditLogs(
              AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,EntityId,
              OldValues,NewValues,CorrelationId,Timestamp)
            VALUES(
              @AuditLogId,@UserId,@TenantId,@BusinessId,N'Order.Cancelled',N'Order',
              CONVERT(nvarchar(36),@OrderId),
              CONCAT(N'{"status":',@OldStatus,N'}'),
              CONCAT(N'{"status":6,"reason":"',STRING_ESCAPE(@Reason,'json'),N'"}'),
              @CorrelationId,@Now);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@AuditLogId", ids.NewId());
            command.Parameters.AddWithValue("@UserId", actor.UserId);
            command.Parameters.AddWithValue("@TenantId", actor.TenantId);
            command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
            command.Parameters.AddWithValue("@OrderId", orderId);
            command.Parameters.AddWithValue("@OldStatus", status);
            command.Parameters.AddWithValue("@Reason", reason);
            command.Parameters.AddWithValue("@CorrelationId", $"order-cancel:{idempotencyKey}");
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var reportingVersion = await orderReportingJobs.EnsureAsync(
            connection,
            transaction,
            actor.TenantId,
            actor.BusinessId,
            orderId,
            cancellationToken);
        return new(orderId, orderNumber, reportingVersion, false);
    }

    public async Task PrepareSourceOrderInventoryAsync(
        OnlineSalesUserIdentity user,
        Guid businessId,
        Guid orderId,
        Guid destinationWarehouseId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ReleaseOrderInventoryCoreAsync(
                connection, transaction, user, orderId, businessId,
                destinationWarehouseId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ReleaseOrderInventoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        DraftState state,
        CancellationToken cancellationToken)
    {
        if (state.SourceOrderId is null) return;
        await ReleaseOrderInventoryCoreAsync(
            connection, transaction, user, state.SourceOrderId.Value,
            state.BusinessId, state.WarehouseId, cancellationToken);
    }

    private async Task ReleaseOrderInventoryCoreAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        Guid orderId,
        Guid businessId,
        Guid destinationWarehouseId,
        CancellationToken cancellationToken)
    {

        const string orderSql = """
            SELECT OrdersWarehouseId,ReleaseTransferId,ReservationTransferId,ExternalStatus,
                   COALESCE(NULLIF(ExternalDocumentNumber,N''),
                            CONCAT(N'PED-',LEFT(CONVERT(nvarchar(36),OrderId),8)))
            FROM dbo.Orders WITH(UPDLOCK,HOLDLOCK)
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
            """;
        Guid ordersWarehouseId; Guid? existingTransferId; Guid? reservationTransferId;
        string? externalStatus; string orderNumber;
        await using (var command = new SqlCommand(orderSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@OrderId", orderId);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new OnlineSalesDraftValidationException("El pedido de origen no existe en este negocio.");
            // Legacy/imported orders may not own a PED reservation. They remain
            // recoverable, but there is no inventory movement to release.
            if (reader.IsDBNull(0)) return;
            ordersWarehouseId = reader.GetGuid(0);
            existingTransferId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            reservationTransferId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
            externalStatus = reader.IsDBNull(3) ? null : reader.GetString(3);
            orderNumber = reader.GetString(4);
        }
        if (externalStatus == "InventoryReleasedForInvoice") return;
        if (existingTransferId is not null)
            throw new OnlineSalesDraftConcurrencyException("El traslado de salida del pedido quedó en un estado inconsistente.");

        const string productsSql = """
            SELECT item.ProductId,SUM(COALESCE(
                     TRY_CONVERT(DECIMAL(19,6),JSON_VALUE(
                       CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
                       '$.ReservedQuantity')),
                     CASE WHEN orders.Status=2 THEN item.Quantity ELSE 0 END))
            FROM dbo.OrderItems item WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Orders orders WITH(UPDLOCK,HOLDLOCK)
              ON orders.OrderId=item.OrderId AND orders.BusinessId=item.BusinessId
            INNER JOIN dbo.Products product WITH(UPDLOCK,HOLDLOCK)
              ON product.ProductId=item.ProductId
             AND product.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=item.BusinessId)
            WHERE item.OrderId=@OrderId AND item.BusinessId=@BusinessId AND product.ManageStock=1
            GROUP BY item.ProductId
            HAVING SUM(COALESCE(
                     TRY_CONVERT(DECIMAL(19,6),JSON_VALUE(
                       CASE WHEN ISJSON(item.RawPayloadJson)=1 THEN item.RawPayloadJson END,
                       '$.ReservedQuantity')),
                     CASE WHEN orders.Status=2 THEN item.Quantity ELSE 0 END))>0
            ORDER BY item.ProductId;
            """;
        var inventoryLines = new List<(Guid ProductId, decimal Quantity)>();
        await using (var command = new SqlCommand(productsSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@OrderId", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                inventoryLines.Add((reader.GetGuid(0), reader.GetDecimal(1)));
        }
        var lines = inventoryLines
            .Select((line, index) => new WarehouseTransferLineRequest(index + 1, line.ProductId, line.Quantity))
            .ToArray();
        if (lines.Length == 0)
        {
            await MarkOrderInventoryReleasedAsync(connection, transaction, orderId, businessId,
                null, cancellationToken);
            return;
        }

        var transferId = ids.NewId();
        var identity = new InventoryUserIdentity(user.UserId, user.TenantId, businessId,
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryPermissionCodes.DispatchTransfer,
                "inventory.system-warehouses.use"
            });
        try
        {
            var releaseKey = $"seller-order-release:{orderId:N}:" +
                (reservationTransferId?.ToString("N") ?? "legacy");
            await inventoryOperations.ConfirmSystemTransferAtomicallyAsync(identity,
                releaseKey,
                new DispatchWarehouseTransferRequest(transferId, businessId, ordersWarehouseId,
                    destinationWarehouseId, time.GetUtcNow(), "WAREHOUSE_TRANSFER",
                    $"Salida completa del pedido {orderNumber} para facturación", lines),
                connection, transaction, cancellationToken);
        }
        catch (InventoryValidationException exception)
        {
            throw new OnlineSalesDraftValidationException(exception.Message);
        }
        catch (InventoryConflictException exception)
        {
            throw new OnlineSalesDraftConcurrencyException(exception.Message);
        }
        await MarkOrderInventoryReleasedAsync(connection, transaction, orderId, businessId,
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
