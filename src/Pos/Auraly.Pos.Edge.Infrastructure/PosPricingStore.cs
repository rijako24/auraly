using System.Globalization;
using Auraly.Contracts.Catalog;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed partial class PosCatalogStore
{
    public async Task ApplyPricingSnapshotAsync(
        PosPricingSnapshot snapshot,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await ExecutePricingAsync(connection, transaction, """
            DELETE FROM PosPriceListItems;
            DELETE FROM PosPriceChannelItems;
            DELETE FROM PosPricingCustomers;
            """, [], ct);
        foreach (var item in snapshot.PriceListItems)
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPriceListItems(PriceListId,ProductId,MinimumQuantity,Amount,CurrencyCode)
                VALUES(@PriceListId,@ProductId,@MinimumQuantity,@Amount,@CurrencyCode);
                """,
                [Q("@PriceListId", item.PriceListId), Q("@ProductId", item.ProductId),
                 Q("@MinimumQuantity", item.MinimumQuantity), Q("@Amount", item.Amount),
                 Q("@CurrencyCode", item.CurrencyCode)], ct);
        foreach (var item in snapshot.PriceChannelItems)
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPriceChannelItems(PriceChannelId,ProductId,Amount,CurrencyCode,IsExcluded)
                VALUES(@PriceChannelId,@ProductId,@Amount,@CurrencyCode,@IsExcluded);
                """,
                [Q("@PriceChannelId", item.PriceChannelId), Q("@ProductId", item.ProductId),
                 Q("@Amount", item.Amount), Q("@CurrencyCode", item.CurrencyCode),
                 Q("@IsExcluded", item.IsExcluded ? 1 : 0)], ct);
        foreach (var customer in snapshot.Customers)
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPricingCustomers(CustomerId,Identification,Name,PriceListId,PriceChannelId,IsActive)
                VALUES(@CustomerId,@Identification,@Name,@PriceListId,@PriceChannelId,@IsActive);
                """,
                [Q("@CustomerId", customer.CustomerId), Q("@Identification", customer.Identification),
                 Q("@Name", customer.Name), Q("@PriceListId", customer.PriceListId),
                 Q("@PriceChannelId", customer.PriceChannelId), Q("@IsActive", customer.IsActive ? 1 : 0)], ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<PosResolvedPrice> ResolvePriceAsync(
        Guid productId,
        Guid? customerId,
        decimal quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT UnitPrice,CurrencyCode FROM PosCatalogProducts
            WHERE ProductId=@ProductId AND IsActive=1;
            """;
        command.Parameters.Add(Q("@ProductId", productId));
        await using var baseReader = await command.ExecuteReaderAsync(ct);
        if (!await baseReader.ReadAsync(ct))
            throw new KeyNotFoundException("The product is not available in the local catalog.");
        var baseAmount = Convert.ToDecimal(baseReader.GetValue(0), CultureInfo.InvariantCulture);
        var currency = baseReader.GetString(1);
        await baseReader.DisposeAsync();
        if (customerId is null)
            return new(productId, baseAmount, baseAmount, currency, "Base", null, null);

        command.Parameters.Clear();
        command.CommandText = """
            SELECT PriceListId,PriceChannelId FROM PosPricingCustomers
            WHERE CustomerId=@CustomerId AND IsActive=1;
            """;
        command.Parameters.Add(Q("@CustomerId", customerId));
        await using var customerReader = await command.ExecuteReaderAsync(ct);
        if (!await customerReader.ReadAsync(ct))
            return new(productId, baseAmount, baseAmount, currency, "Base", null, null);
        var listId = customerReader.IsDBNull(0) ? (Guid?)null : Guid.Parse(customerReader.GetString(0));
        var channelId = customerReader.IsDBNull(1) ? (Guid?)null : Guid.Parse(customerReader.GetString(1));
        await customerReader.DisposeAsync();

        command.Parameters.Clear();
        if (listId is not null)
        {
            command.CommandText = """
                SELECT Amount,CurrencyCode FROM PosPriceListItems
                WHERE PriceListId=@SourceId AND ProductId=@ProductId AND MinimumQuantity<=@Quantity
                ORDER BY MinimumQuantity DESC LIMIT 1;
                """;
            command.Parameters.AddRange(
                [Q("@SourceId", listId), Q("@ProductId", productId), Q("@Quantity", quantity)]);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return new(productId, baseAmount,
                    Convert.ToDecimal(reader.GetValue(0), CultureInfo.InvariantCulture),
                    reader.GetString(1), "PriceList", listId, null);
        }
        else if (channelId is not null)
        {
            command.CommandText = """
                SELECT Amount,CurrencyCode FROM PosPriceChannelItems
                WHERE PriceChannelId=@SourceId AND ProductId=@ProductId AND IsExcluded=0 LIMIT 1;
                """;
            command.Parameters.AddRange([Q("@SourceId", channelId), Q("@ProductId", productId)]);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return new(productId, baseAmount,
                    Convert.ToDecimal(reader.GetValue(0), CultureInfo.InvariantCulture),
                    reader.GetString(1), "PriceChannel", null, channelId);
        }
        return new(productId, baseAmount, baseAmount, currency, "Base", listId, channelId);
    }

    private static async Task InitializePricingAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PosPricingCustomers(
              CustomerId TEXT PRIMARY KEY,Identification TEXT NOT NULL,Name TEXT NOT NULL,
              PriceListId TEXT NULL,PriceChannelId TEXT NULL,IsActive INTEGER NOT NULL,
              CHECK(PriceListId IS NULL OR PriceChannelId IS NULL));
            CREATE INDEX IF NOT EXISTS IX_PosPricingCustomers_Identification ON PosPricingCustomers(Identification);
            CREATE TABLE IF NOT EXISTS PosPriceListItems(
              PriceListId TEXT NOT NULL,ProductId TEXT NOT NULL,MinimumQuantity TEXT NOT NULL,
              Amount TEXT NOT NULL,CurrencyCode TEXT NOT NULL,
              PRIMARY KEY(PriceListId,ProductId,MinimumQuantity));
            CREATE TABLE IF NOT EXISTS PosPriceChannelItems(
              PriceChannelId TEXT NOT NULL,ProductId TEXT NOT NULL,Amount TEXT NOT NULL,
              CurrencyCode TEXT NOT NULL,IsExcluded INTEGER NOT NULL,
              PRIMARY KEY(PriceChannelId,ProductId));
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecutePricingAsync(
        SqliteConnection connection, System.Data.Common.DbTransaction transaction,
        string sql, SqliteParameter[] parameters, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static SqliteParameter Q(string name, object? value) =>
        new(name, value switch { Guid id => id.ToString("D"), _ => value ?? DBNull.Value });
}
