using System.Globalization;
using Auraly.Contracts.Catalog;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed partial class PosCatalogStore
{
    public async Task<IReadOnlyCollection<PosCustomerPricing>> SearchCustomersAsync(
        string term,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
        if (take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CustomerId,Identification,Name,PriceListId,PriceChannelId,RequiresElectronicInvoice,IsActive
            FROM PosPricingCustomers
            WHERE IsActive=1
              AND (@Term='' OR Identification LIKE @Prefix OR Name LIKE @Name)
            ORDER BY CASE WHEN Identification=@Term THEN 0 ELSE 1 END,Name,CustomerId
            LIMIT @Take OFFSET @Skip;
            """;
        var normalized = term.Trim();
        command.Parameters.AddRange([
            Q("@Term", normalized),
            Q("@Prefix", $"{normalized}%"),
            Q("@Name", $"%{normalized}%"),
            Q("@Take", take),
            Q("@Skip", skip)
        ]);
        var customers = new List<PosCustomerPricing>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            customers.Add(ReadCustomer(reader));
        return customers;
    }

    public async Task<PosCustomerPricing?> GetCustomerAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CustomerId,Identification,Name,PriceListId,PriceChannelId,RequiresElectronicInvoice,IsActive
            FROM PosPricingCustomers
            WHERE CustomerId=@CustomerId AND IsActive=1;
            """;
        command.Parameters.Add(Q("@CustomerId", customerId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadCustomer(reader) : null;
    }

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
                INSERT INTO PosPricingCustomers(CustomerId,Identification,Name,PriceListId,PriceChannelId,RequiresElectronicInvoice,IsActive)
                VALUES(@CustomerId,@Identification,@Name,@PriceListId,@PriceChannelId,@RequiresElectronicInvoice,@IsActive);
                """,
                [Q("@CustomerId", customer.CustomerId), Q("@Identification", customer.Identification),
                 Q("@Name", customer.Name), Q("@PriceListId", customer.PriceListId),
                 Q("@PriceChannelId", customer.PriceChannelId),
                 Q("@RequiresElectronicInvoice", customer.RequiresElectronicInvoice ? 1 : 0),
                 Q("@IsActive", customer.IsActive ? 1 : 0)], ct);
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
              PriceListId TEXT NULL,PriceChannelId TEXT NULL,RequiresElectronicInvoice INTEGER NOT NULL DEFAULT 0,IsActive INTEGER NOT NULL,
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
        command.CommandText = "PRAGMA table_info(PosPricingCustomers);";
        var hasBillingColumn = false;
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                hasBillingColumn |= string.Equals(reader.GetString(1), "RequiresElectronicInvoice", StringComparison.Ordinal);
        if (!hasBillingColumn)
        {
            command.CommandText = "ALTER TABLE PosPricingCustomers ADD COLUMN RequiresElectronicInvoice INTEGER NOT NULL DEFAULT 0;";
            await command.ExecuteNonQueryAsync(ct);
        }
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

    private static PosCustomerPricing ReadCustomer(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
            reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
            reader.GetInt32(6) == 1,
            reader.GetInt32(5) == 1);
}
