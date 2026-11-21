using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed record EditableSellerOrder(
    string Number,
    Guid CustomerId,
    Guid PartySiteId,
    int Status,
    Guid WarehouseId,
    Guid OrdersWarehouseId,
    IReadOnlyList<EditableSellerOrderLine> Lines, Guid? SellerId = null);

public sealed record EditableSellerOrderLine(
    int Position,
    Guid ProductId,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    string PriceSource,
    decimal DocumentUnitCost,
    bool ManageStock,
    string Description,
    bool IsGenericProductSnapshot);

public sealed record SellerOrderReplacementLine(
    Guid ProductId,
    string Code,
    string Name,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DocumentUnitCost,
    decimal DiscountAmount,
    decimal LineTotal,
    decimal TaxAmount,
    string RawPayloadJson,
    bool IsGenericProductSnapshot);

public static class SellerOrderReviewPersistence
{
    public static async Task<EditableSellerOrder?> FindEditableAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid orderId,
        Guid businessId,
        Guid userId,
        Guid? workSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = Procedure("dbo.SellerOrderEditableGet", connection, transaction);
        command.Parameters.AddRange([
            Parameter("@OrderId", orderId),
            Parameter("@BusinessId", businessId),
            Parameter("@UserId", userId),
            Parameter("@WorkSessionId", workSessionId)
        ]);
        string number;
        Guid customerId;
        Guid partySiteId;
        int status;
        Guid warehouseId;
        Guid ordersWarehouseId;
        Guid? sellerId;
        var lines = new List<EditableSellerOrderLine>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(4) || reader.IsDBNull(5))
                return null;
            number = reader.GetString(0);
            customerId = reader.GetGuid(1);
            partySiteId = reader.GetGuid(2);
            status = reader.GetInt32(3);
            warehouseId = reader.GetGuid(4);
            ordersWarehouseId = reader.GetGuid(5);
            sellerId = reader.IsDBNull(6) ? null : reader.GetGuid(6);
            if (!await reader.NextResultAsync(cancellationToken))
                return null;
            while (await reader.ReadAsync(cancellationToken))
            {
                var line = new EditableSellerOrderLine(
                    reader.GetInt32(0),
                    reader.GetGuid(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetString(6),
                    reader.GetDecimal(7),
                    reader.GetBoolean(8),
                    reader.GetString(9),
                    reader.GetBoolean(10));
                lines.Add(line);
            }
        }
        return new EditableSellerOrder(number, customerId, partySiteId, status, warehouseId, ordersWarehouseId, lines, sellerId);
    }

    public static async Task<string?> ReadMutationReceiptAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid orderId, Guid userId, string key, string hash, CancellationToken ct)
    {
        await using var command = Procedure("dbo.SellerOrderMutationReceiptGet", connection, transaction);
        command.Parameters.AddRange([Parameter("@BusinessId",businessId),Parameter("@IdempotencyKey",key)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        if (reader.GetGuid(0)!=orderId || reader.GetGuid(1)!=userId || reader.GetString(2)!=hash)
            throw new InvalidOperationException("La clave de actualización del pedido ya se usó con otra operación.");
        return reader.GetString(3);
    }

    public static async Task SaveMutationReceiptAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid orderId, Guid userId, string key, string hash, string resultJson, CancellationToken ct)
    {
        await using var command = Procedure("dbo.SellerOrderMutationReceiptSave", connection, transaction);
        command.Parameters.AddRange([Parameter("@BusinessId",businessId),Parameter("@OrderId",orderId),
            Parameter("@UserId",userId),Parameter("@IdempotencyKey",key),Parameter("@RequestHash",hash),Parameter("@ResultJson",resultJson)]);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task UpdateMetadataAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid orderId,
        Guid businessId,
        string? notes,
        Guid userId,
        Guid? workSessionId,
        CancellationToken cancellationToken, Guid? requestedSellerId = null)
    {
        await using var command = Procedure("dbo.SellerOrderMetadataUpdate", connection, transaction);
        command.Parameters.AddRange([
            Parameter("@OrderId", orderId),
            Parameter("@BusinessId", businessId),
            Parameter("@RequestedSellerId", requestedSellerId),
            Parameter("@Notes", notes),
            Parameter("@UserId", userId),
            Parameter("@WorkSessionId", workSessionId)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ReplaceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid orderId,
        Guid businessId,
        Guid customerId,
        Guid partySiteId,
        string customerName,
        string? customerIdentification,
        string? customerEmail,
        string? customerPhone,
        string customerAddress,
        string? notes,
        decimal total,
        Guid reservationTransferId,
        int status,
        string externalStatus,
        bool requiresStockReview,
        IReadOnlyCollection<SellerOrderReplacementLine> lines,
        Guid userId,
        Guid? workSessionId,
        CancellationToken cancellationToken, Guid? requestedSellerId = null)
    {
        await using (var update = Procedure("dbo.SellerOrderReplace", connection, transaction))
        {
            update.Parameters.AddRange([
                Parameter("@RequestedSellerId", requestedSellerId),
                Parameter("@Notes", notes),
                Parameter("@CustomerId", customerId),
                Parameter("@PartySiteId", partySiteId),
                Parameter("@CustomerName", customerName),
                Parameter("@CustomerIdentification", customerIdentification),
                Parameter("@CustomerEmail", customerEmail),
                Parameter("@CustomerPhone", customerPhone),
                Parameter("@CustomerAddress", customerAddress),
                Money("@Total", total),
                Parameter("@ReservationTransferId", reservationTransferId),
                Parameter("@Status", status),
                Parameter("@ExternalStatus", externalStatus),
                Parameter("@RequiresStockReview", requiresStockReview),
                Parameter("@OrderId", orderId),
                Parameter("@BusinessId", businessId),
                Parameter("@UserId", userId),
                Parameter("@WorkSessionId", workSessionId),
                Parameter("@LinesJson", System.Text.Json.JsonSerializer.Serialize(lines.Select(line => new
                {
                    productId = line.ProductId,
                    code = line.Code,
                    name = line.Name,
                    unitCode = line.UnitCode,
                    quantity = line.Quantity,
                    unitPrice = line.UnitPrice,
                    documentUnitCost = line.DocumentUnitCost,
                    discountAmount = line.DiscountAmount,
                    lineTotal = line.LineTotal,
                    taxAmount = line.TaxAmount,
                    isGenericProductSnapshot = line.IsGenericProductSnapshot,
                    rawPayloadJson = line.RawPayloadJson
                })))
            ]);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static SqlParameter Parameter(string name, object? value) => new(name, value ?? DBNull.Value);
    private static SqlParameter Money(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = value };
    private static SqlCommand Procedure(string name, SqlConnection connection, SqlTransaction? transaction = null) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure };
}
