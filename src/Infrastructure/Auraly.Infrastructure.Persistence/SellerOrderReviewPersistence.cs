using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed record EditableSellerOrder(
    string Number,
    Guid CustomerId,
    int Status,
    Guid WarehouseId,
    Guid OrdersWarehouseId);

public sealed record SellerOrderReplacementLine(
    Guid ProductId,
    string Code,
    string Name,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string RawPayloadJson);

public static class SellerOrderReviewPersistence
{
    public static async Task<EditableSellerOrder?> FindEditableAsync(
        SqlServerConnectionFactory connections,
        Guid orderId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT o.ExternalDocumentNumber,o.CustomerId,o.Status,
                   TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.WarehouseId')),
                   TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.ordersWarehouseId'))
            FROM dbo.Orders o
            WHERE o.OrderId=@Id AND o.BusinessId=@BusinessId AND o.Source=1
              AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.createdBy'))=@UserId
              AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link WHERE link.OrderId=o.OrderId);
            """, connection);
        command.Parameters.AddRange([
            Parameter("@Id", orderId),
            Parameter("@BusinessId", businessId),
            Parameter("@UserId", userId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(1) || reader.IsDBNull(3) || reader.IsDBNull(4))
            return null;
        return new EditableSellerOrder(reader.GetString(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetGuid(3), reader.GetGuid(4));
    }

    public static async Task ReplaceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid orderId,
        Guid businessId,
        string? notes,
        decimal total,
        Guid reservationTransferId,
        IReadOnlyCollection<SellerOrderReplacementLine> lines,
        CancellationToken cancellationToken)
    {
        await using (var update = new SqlCommand("""
            UPDATE dbo.Orders SET Notes=@Notes,Subtotal=@Total,Total=@Total,Status=5,ExternalStatus=N'InventoryTransferPending',
              CustomAttributesJson=JSON_MODIFY(JSON_MODIFY(CustomAttributesJson,'$.reservationTransferId',CONVERT(nvarchar(36),@TransferId)),'$.requiresStockReview',CAST(0 AS bit)),UpdatedAt=SYSUTCDATETIME()
            WHERE OrderId=@Id AND BusinessId=@BusinessId;
            DELETE dbo.OrderItems WHERE OrderId=@Id;
            """, connection, transaction))
        {
            update.Parameters.AddRange([
                Parameter("@Notes", notes),
                Money("@Total", total),
                Parameter("@TransferId", reservationTransferId),
                Parameter("@Id", orderId),
                Parameter("@BusinessId", businessId)
            ]);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in lines)
        {
            await using var insert = new SqlCommand("""
                INSERT dbo.OrderItems(OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,ProductNameSnapshot,DescriptionSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,DiscountAmount,TaxAmount,LineTotal,RawPayloadJson,CreatedAt)
                VALUES(NEWID(),@OrderId,@BusinessId,@ProductId,@Sku,@Code,@Name,@Name,@Unit,@Quantity,@Price,0,0,@Total,@Raw,SYSUTCDATETIME());
                """, connection, transaction);
            insert.Parameters.AddRange([
                Parameter("@OrderId", orderId),
                Parameter("@BusinessId", businessId),
                Parameter("@ProductId", line.ProductId),
                Parameter("@Sku", line.Code),
                Parameter("@Code", line.Code),
                Parameter("@Name", line.Name),
                Parameter("@Unit", line.UnitCode),
                Quantity("@Quantity", line.Quantity),
                Money("@Price", line.UnitPrice),
                Money("@Total", line.LineTotal),
                Parameter("@Raw", line.RawPayloadJson)
            ]);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static SqlParameter Parameter(string name, object? value) => new(name, value ?? DBNull.Value);
    private static SqlParameter Money(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = value };
    private static SqlParameter Quantity(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 19, Scale = 6, Value = value };
}
