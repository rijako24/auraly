using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed record EditableSellerOrder(
    string Number,
    Guid CustomerId,
    int Status,
    Guid WarehouseId,
    Guid OrdersWarehouseId,
    IReadOnlyDictionary<Guid, EditableSellerOrderLine> Lines);

public sealed record EditableSellerOrderLine(
    Guid ProductId,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    string PriceSource,
    bool ManageStock);

public sealed record SellerOrderReplacementLine(
    Guid ProductId,
    string Code,
    string Name,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal,
    string RawPayloadJson);

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
        int status;
        Guid warehouseId;
        Guid ordersWarehouseId;
        var lines = new Dictionary<Guid, EditableSellerOrderLine>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(1) || reader.IsDBNull(3) || reader.IsDBNull(4))
                return null;
            number = reader.GetString(0);
            customerId = reader.GetGuid(1);
            status = reader.GetInt32(2);
            warehouseId = reader.GetGuid(3);
            ordersWarehouseId = reader.GetGuid(4);
            if (!await reader.NextResultAsync(cancellationToken))
                return null;
            while (await reader.ReadAsync(cancellationToken))
            {
                var line = new EditableSellerOrderLine(
                    reader.GetGuid(0),
                    reader.GetDecimal(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetString(5),
                    reader.GetBoolean(6));
                lines[line.ProductId] = line;
            }
        }
        return new EditableSellerOrder(number, customerId, status, warehouseId, ordersWarehouseId, lines);
    }

    public static async Task ReplaceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid orderId,
        Guid businessId,
        Guid customerId,
        string customerName,
        string? customerIdentification,
        string? customerEmail,
        string? customerPhone,
        string customerAddress,
        string? notes,
        decimal total,
        Guid reservationTransferId,
        IReadOnlyCollection<SellerOrderReplacementLine> lines,
        CancellationToken cancellationToken)
    {
        await using (var update = Procedure("dbo.SellerOrderReplace", connection, transaction))
        {
            update.Parameters.AddRange([
                Parameter("@Notes", notes),
                Parameter("@CustomerId", customerId),
                Parameter("@CustomerName", customerName),
                Parameter("@CustomerIdentification", customerIdentification),
                Parameter("@CustomerEmail", customerEmail),
                Parameter("@CustomerPhone", customerPhone),
                Parameter("@CustomerAddress", customerAddress),
                Money("@Total", total),
                Parameter("@ReservationTransferId", reservationTransferId),
                Parameter("@OrderId", orderId),
                Parameter("@BusinessId", businessId),
                Parameter("@LinesJson", System.Text.Json.JsonSerializer.Serialize(lines.Select(line => new
                {
                    productId = line.ProductId,
                    code = line.Code,
                    name = line.Name,
                    unitCode = line.UnitCode,
                    quantity = line.Quantity,
                    unitPrice = line.UnitPrice,
                    discountAmount = line.DiscountAmount,
                    lineTotal = line.LineTotal,
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
