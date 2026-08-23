using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed record EditableSellerOrder(
    string Number,
    Guid CustomerId,
    int Status,
    Guid WarehouseId,
    Guid OrdersWarehouseId,
    IReadOnlyDictionary<Guid, decimal> ReservedQuantities);

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
        Guid? workSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.SellerOrderEditableGet", connection);
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
        var reserved = new Dictionary<Guid, decimal>();
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
                reserved[reader.GetGuid(0)] = reader.GetDecimal(1);
        }
        return new EditableSellerOrder(number, customerId, status, warehouseId, ordersWarehouseId, reserved);
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
        await using (var update = Procedure("dbo.SellerOrderReplace", connection, transaction))
        {
            update.Parameters.AddRange([
                Parameter("@Notes", notes),
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
