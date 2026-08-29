using System.Globalization;
using System.Text.Json;
using Auraly.Contracts.Catalog;
using Auraly.Commerce.Taxation.Domain;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed partial class PosCatalogStore
{
    public async Task<PosPricingSnapshot> ReadPricingSnapshotAsync(
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        var customers = new List<PosCustomerPricing>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT CustomerId,Identification,Name,PriceChannelId,RequiresElectronicInvoice,IsActive,
                       AppliesWithholding,TaxResponsibilities,TaxJurisdictionCode
                FROM PosPricingCustomers;
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) customers.Add(ReadCustomer(reader));
        }

        var prices = new List<PosPriceChannelItem>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT PriceChannelId,ProductId,MinimumQuantity,Amount,CurrencyCode,IsExcluded
                FROM PosPriceChannelItems;
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                prices.Add(new(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture),
                    reader.GetString(4),
                    reader.GetInt32(5) == 1));
        }

        var rules = new List<PosWithholdingRule>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT RuleId,Version,Code,Name,Kind,Direction,Moment,BaseKind,ConceptCode,
                       JurisdictionCode,Rate,MinimumBase,RequiredResponsibilities,EffectiveFrom,
                       EffectiveTo,IsActive
                FROM PosWithholdingRules;
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rules.Add(new(
                    Guid.Parse(reader.GetString(0)), reader.GetInt32(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(reader.GetValue(11), CultureInfo.InvariantCulture),
                    JsonSerializer.Deserialize<string[]>(reader.GetString(12)) ?? [],
                    DateOnly.ParseExact(reader.GetString(13), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.IsDBNull(14) ? null : DateOnly.ParseExact(
                        reader.GetString(14), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.GetInt32(15) == 1));
        }

        return new(prices, customers, rules);
    }

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
            SELECT CustomerId,Identification,Name,PriceChannelId,RequiresElectronicInvoice,IsActive,
                   AppliesWithholding,TaxResponsibilities,TaxJurisdictionCode
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
            SELECT CustomerId,Identification,Name,PriceChannelId,RequiresElectronicInvoice,IsActive,
                   AppliesWithholding,TaxResponsibilities,TaxJurisdictionCode
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
            DELETE FROM PosPriceChannelItems;
            DELETE FROM PosWithholdingRules;
            DELETE FROM PosPricingCustomers;
            """, [], ct);
        foreach (var item in snapshot.PriceChannelItems)
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPriceChannelItems(PriceChannelId,ProductId,MinimumQuantity,Amount,CurrencyCode,IsExcluded)
                VALUES(@PriceChannelId,@ProductId,@MinimumQuantity,@Amount,@CurrencyCode,@IsExcluded);
                """,
                [Q("@PriceChannelId", item.PriceChannelId), Q("@ProductId", item.ProductId),
                 Q("@MinimumQuantity", item.MinimumQuantity), Q("@Amount", item.Amount), Q("@CurrencyCode", item.CurrencyCode),
                 Q("@IsExcluded", item.IsExcluded ? 1 : 0)], ct);
        foreach (var customer in snapshot.Customers)
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPricingCustomers(
                  CustomerId,Identification,Name,PriceChannelId,RequiresElectronicInvoice,IsActive,
                  AppliesWithholding,TaxResponsibilities,TaxJurisdictionCode)
                VALUES(@CustomerId,@Identification,@Name,@PriceChannelId,@RequiresElectronicInvoice,@IsActive,
                  @AppliesWithholding,@TaxResponsibilities,@TaxJurisdictionCode);
                """,
                [Q("@CustomerId", customer.CustomerId), Q("@Identification", customer.Identification),
                 Q("@Name", customer.Name), Q("@PriceChannelId", customer.PriceChannelId),
                 Q("@RequiresElectronicInvoice", customer.RequiresElectronicInvoice ? 1 : 0),
                 Q("@IsActive", customer.IsActive ? 1 : 0),
                 Q("@AppliesWithholding", customer.AppliesWithholding ? 1 : 0),
                 Q("@TaxResponsibilities", JsonSerializer.Serialize(customer.TaxResponsibilities ?? [])),
                 Q("@TaxJurisdictionCode", customer.TaxJurisdictionCode)], ct);
        foreach (var rule in snapshot.WithholdingRules ?? [])
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosWithholdingRules(
                  RuleId,Version,Code,Name,Kind,Direction,Moment,BaseKind,ConceptCode,
                  JurisdictionCode,Rate,MinimumBase,RequiredResponsibilities,EffectiveFrom,
                  EffectiveTo,IsActive)
                VALUES(@RuleId,@Version,@Code,@Name,@Kind,@Direction,@Moment,@BaseKind,@ConceptCode,
                  @JurisdictionCode,@Rate,@MinimumBase,@RequiredResponsibilities,@EffectiveFrom,
                  @EffectiveTo,@IsActive);
                """,
                [Q("@RuleId", rule.RuleId), Q("@Version", rule.Version), Q("@Code", rule.Code),
                 Q("@Name", rule.Name), Q("@Kind", rule.Kind), Q("@Direction", rule.Direction),
                 Q("@Moment", rule.Moment), Q("@BaseKind", rule.BaseKind),
                 Q("@ConceptCode", rule.ConceptCode), Q("@JurisdictionCode", rule.JurisdictionCode),
                 Q("@Rate", rule.Rate), Q("@MinimumBase", rule.MinimumBase),
                 Q("@RequiredResponsibilities", JsonSerializer.Serialize(rule.RequiredResponsibilities)),
                 Q("@EffectiveFrom", rule.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                 Q("@EffectiveTo", rule.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                 Q("@IsActive", rule.IsActive ? 1 : 0)], ct);
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
            return new(productId, baseAmount, baseAmount, currency, "Base", null);

        command.Parameters.Clear();
        command.CommandText = """
            SELECT PriceChannelId FROM PosPricingCustomers
            WHERE CustomerId=@CustomerId AND IsActive=1;
            """;
        command.Parameters.Add(Q("@CustomerId", customerId));
        await using var customerReader = await command.ExecuteReaderAsync(ct);
        if (!await customerReader.ReadAsync(ct))
            return new(productId, baseAmount, baseAmount, currency, "Base", null);
        var channelId = customerReader.IsDBNull(0) ? (Guid?)null : Guid.Parse(customerReader.GetString(0));
        await customerReader.DisposeAsync();

        command.Parameters.Clear();
        if (channelId is not null)
        {
            command.CommandText = """
                SELECT Amount,CurrencyCode FROM PosPriceChannelItems
                WHERE PriceChannelId=@SourceId AND ProductId=@ProductId AND MinimumQuantity<=@Quantity AND IsExcluded=0
                ORDER BY MinimumQuantity DESC LIMIT 1;
                """;
            command.Parameters.AddRange([Q("@SourceId", channelId), Q("@ProductId", productId), Q("@Quantity", quantity)]);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return new(productId, baseAmount,
                    Convert.ToDecimal(reader.GetValue(0), CultureInfo.InvariantCulture),
                    reader.GetString(1), "PriceChannel", channelId);
        }
        return new(productId, baseAmount, baseAmount, currency, "Base", channelId);
    }

    public async Task<WithholdingCalculation> CalculateSaleWithholdingAsync(
        Guid businessId,
        Guid? customerId,
        decimal taxExclusiveAmount,
        decimal vatAmount,
        DateTimeOffset occurredAt,
        CancellationToken ct = default)
    {
        var gross = decimal.Round(
            taxExclusiveAmount + vatAmount, 4, MidpointRounding.AwayFromZero);
        if (customerId is null)
            return new WithholdingCalculation(gross, 0m, gross, []);

        var snapshot = await ReadPricingSnapshotAsync(ct);
        var customer = snapshot.Customers.SingleOrDefault(
            item => item.CustomerId == customerId.Value && item.IsActive);
        if (customer is null)
            throw new KeyNotFoundException("The customer is not available in the local projection.");

        var context = new WithholdingCalculationContext(
            businessId,
            WithholdingDirection.Sale,
            WithholdingRecognitionMoment.Accrual,
            customer.CustomerId,
            null,
            customer.TaxJurisdictionCode,
            taxExclusiveAmount,
            vatAmount,
            occurredAt,
            customer.AppliesWithholding,
            new HashSet<string>(customer.TaxResponsibilities ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<Guid>());
        var rules = (snapshot.WithholdingRules ?? [])
            .Select(rule => WithholdingRule.Create(
                rule.RuleId,
                businessId,
                rule.Version,
                rule.Code,
                rule.Name,
                Enum.Parse<WithholdingKind>(rule.Kind),
                Enum.Parse<WithholdingDirection>(rule.Direction),
                Enum.Parse<WithholdingRecognitionMoment>(rule.Moment),
                Enum.Parse<WithholdingBaseKind>(rule.BaseKind),
                rule.ConceptCode,
                rule.JurisdictionCode,
                rule.Rate,
                rule.MinimumBase,
                rule.RequiredResponsibilities,
                rule.EffectiveFrom,
                rule.EffectiveTo,
                rule.IsActive))
            .ToArray();
        return new WithholdingEngine().Calculate(context, rules);
    }

    private static async Task InitializePricingAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PosPricingCustomers(
              CustomerId TEXT PRIMARY KEY,Identification TEXT NOT NULL,Name TEXT NOT NULL,
              PriceChannelId TEXT NULL,RequiresElectronicInvoice INTEGER NOT NULL DEFAULT 0,IsActive INTEGER NOT NULL,
              AppliesWithholding INTEGER NOT NULL DEFAULT 0,TaxResponsibilities TEXT NOT NULL DEFAULT '[]',
              TaxJurisdictionCode TEXT NULL);
            CREATE INDEX IF NOT EXISTS IX_PosPricingCustomers_Identification ON PosPricingCustomers(Identification);
            CREATE TABLE IF NOT EXISTS PosPriceChannelItems(
              PriceChannelId TEXT NOT NULL,ProductId TEXT NOT NULL,MinimumQuantity TEXT NOT NULL,
              Amount TEXT NOT NULL,CurrencyCode TEXT NOT NULL,IsExcluded INTEGER NOT NULL,
              PRIMARY KEY(PriceChannelId,ProductId,MinimumQuantity));
            CREATE TABLE IF NOT EXISTS PosWithholdingRules(
              RuleId TEXT NOT NULL,Version INTEGER NOT NULL,Code TEXT NOT NULL,Name TEXT NOT NULL,
              Kind TEXT NOT NULL,Direction TEXT NOT NULL,Moment TEXT NOT NULL,BaseKind TEXT NOT NULL,
              ConceptCode TEXT NULL,JurisdictionCode TEXT NULL,Rate TEXT NOT NULL,MinimumBase TEXT NOT NULL,
              RequiredResponsibilities TEXT NOT NULL,EffectiveFrom TEXT NOT NULL,EffectiveTo TEXT NULL,
              IsActive INTEGER NOT NULL,PRIMARY KEY(RuleId,Version));
            """;
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "PRAGMA table_info(PosPricingCustomers);";
        var hasBillingColumn = false;
        var hasWithholdingColumn = false;
        var hasResponsibilitiesColumn = false;
        var hasJurisdictionColumn = false;
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var column = reader.GetString(1);
                hasBillingColumn |= string.Equals(column, "RequiresElectronicInvoice", StringComparison.Ordinal);
                hasWithholdingColumn |= string.Equals(column, "AppliesWithholding", StringComparison.Ordinal);
                hasResponsibilitiesColumn |= string.Equals(column, "TaxResponsibilities", StringComparison.Ordinal);
                hasJurisdictionColumn |= string.Equals(column, "TaxJurisdictionCode", StringComparison.Ordinal);
            }
        if (!hasBillingColumn)
        {
            command.CommandText = "ALTER TABLE PosPricingCustomers ADD COLUMN RequiresElectronicInvoice INTEGER NOT NULL DEFAULT 0;";
            await command.ExecuteNonQueryAsync(ct);
        }
        if (!hasWithholdingColumn)
        {
            command.CommandText = "ALTER TABLE PosPricingCustomers ADD COLUMN AppliesWithholding INTEGER NOT NULL DEFAULT 0;";
            await command.ExecuteNonQueryAsync(ct);
        }
        if (!hasResponsibilitiesColumn)
        {
            command.CommandText = "ALTER TABLE PosPricingCustomers ADD COLUMN TaxResponsibilities TEXT NOT NULL DEFAULT '[]';";
            await command.ExecuteNonQueryAsync(ct);
        }
        if (!hasJurisdictionColumn)
        {
            command.CommandText = "ALTER TABLE PosPricingCustomers ADD COLUMN TaxJurisdictionCode TEXT NULL;";
            await command.ExecuteNonQueryAsync(ct);
        }
        command.CommandText = "PRAGMA table_info(PosPriceChannelItems);";
        var hasChannelQuantity = false;
        var hasChannelAmount = false;
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                hasChannelQuantity |= string.Equals(reader.GetString(1), "MinimumQuantity", StringComparison.Ordinal);
                hasChannelAmount |= string.Equals(reader.GetString(1), "Amount", StringComparison.Ordinal);
            }
        if (!hasChannelAmount)
        {
            command.CommandText = "ALTER TABLE PosPriceChannelItems ADD COLUMN Amount TEXT NOT NULL DEFAULT '0';";
            await command.ExecuteNonQueryAsync(ct);
        }
        if (!hasChannelQuantity)
        {
            command.CommandText = """
                ALTER TABLE PosPriceChannelItems RENAME TO PosPriceChannelItemsLegacy;
                CREATE TABLE PosPriceChannelItems(
                  PriceChannelId TEXT NOT NULL,ProductId TEXT NOT NULL,MinimumQuantity TEXT NOT NULL,
                  Amount TEXT NOT NULL,CurrencyCode TEXT NOT NULL,IsExcluded INTEGER NOT NULL,
                  PRIMARY KEY(PriceChannelId,ProductId,MinimumQuantity));
                INSERT INTO PosPriceChannelItems(PriceChannelId,ProductId,MinimumQuantity,Amount,CurrencyCode,IsExcluded)
                SELECT PriceChannelId,ProductId,'1',Amount,CurrencyCode,IsExcluded FROM PosPriceChannelItemsLegacy;
                DROP TABLE PosPriceChannelItemsLegacy;
                """;
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
            reader.GetInt32(5) == 1,
            reader.GetInt32(4) == 1,
            reader.GetInt32(6) == 1,
            JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? [],
            reader.IsDBNull(8) ? null : reader.GetString(8));
}
