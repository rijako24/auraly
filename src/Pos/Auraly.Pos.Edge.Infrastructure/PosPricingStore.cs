using System.Globalization;
using System.Text.Json;
using Auraly.Contracts.Catalog;
using Auraly.Domain.Pricing;
using Auraly.Commerce.Taxation.Domain;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Promotions;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosPriceLineRequest(
    string Key, Guid ProductId, decimal Quantity, bool EligibleForPromotion = true);

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

        var channels = new List<PosPriceChannelDefinition>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PriceChannelId,Code,Name,Strategy,Value FROM PosPriceChannels;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                channels.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetString(2),
                    reader.GetString(3),reader.IsDBNull(4) ? null :
                        Convert.ToDecimal(reader.GetValue(4),CultureInfo.InvariantCulture)));
        }

        var tiers = new List<PosPriceChannelTier>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PriceChannelId,ProductId,MinimumQuantity,Amount,CurrencyCode FROM PosPriceChannelTiers;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                tiers.Add(new(Guid.Parse(reader.GetString(0)),Guid.Parse(reader.GetString(1)),
                    Convert.ToDecimal(reader.GetValue(2),CultureInfo.InvariantCulture),
                    Convert.ToDecimal(reader.GetValue(3),CultureInfo.InvariantCulture),reader.GetString(4)));
        }

        var exclusions = new List<PosPriceChannelExclusion>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PriceChannelId,ScopeType,ProductId,ProductCategoryId,ProductBrandId FROM PosPriceChannelExclusions;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                exclusions.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),
                    reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4))));
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

        var promotions = new List<PosPromotion>();
        var allowCombination = false;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT AllowPromotionChannelCombination FROM PosPricingConfiguration WHERE ConfigurationId=1;";
            allowCombination = Convert.ToInt32(await command.ExecuteScalarAsync(ct) ?? 0) == 1;
            command.CommandText = "SELECT Payload FROM PosPromotions ORDER BY Priority DESC,CreatedAtUtc,PromotionId;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                promotions.Add(JsonSerializer.Deserialize<PosPromotion>(reader.GetString(0))
                    ?? throw new InvalidDataException("The local promotion payload is invalid."));
        }
        return new(channels, tiers, exclusions, customers, rules, null, allowCombination, promotions);
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
            DELETE FROM PosPriceChannelTiers;
            DELETE FROM PosPriceChannelExclusions;
            DELETE FROM PosPriceChannels;
            DELETE FROM PosWithholdingRules;
            DELETE FROM PosPricingCustomers;
            DELETE FROM PosPromotions;
            UPDATE PosPricingConfiguration
            SET AllowPromotionChannelCombination=@AllowCombination
            WHERE ConfigurationId=1;
            """, [Q("@AllowCombination", snapshot.AllowPromotionChannelCombination ? 1 : 0)], ct);
        foreach (var channel in snapshot.PriceChannels)
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPriceChannels(PriceChannelId,Code,Name,Strategy,Value)
                VALUES(@PriceChannelId,@Code,@Name,@Strategy,@Value);
                """,
                [Q("@PriceChannelId", channel.PriceChannelId),Q("@Code", channel.Code),
                 Q("@Name", channel.Name),Q("@Strategy", channel.Strategy),Q("@Value", channel.Value)], ct);
        foreach (var tier in snapshot.PriceChannelTiers)
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPriceChannelTiers(PriceChannelId,ProductId,MinimumQuantity,Amount,CurrencyCode)
                VALUES(@PriceChannelId,@ProductId,@MinimumQuantity,@Amount,@CurrencyCode);
                """,
                [Q("@PriceChannelId", tier.PriceChannelId),Q("@ProductId", tier.ProductId),
                 Q("@MinimumQuantity", tier.MinimumQuantity),Q("@Amount", tier.Amount),
                 Q("@CurrencyCode", tier.CurrencyCode)], ct);
        foreach (var exclusion in snapshot.PriceChannelExclusions)
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPriceChannelExclusions(PriceChannelId,ScopeType,ProductId,ProductCategoryId,ProductBrandId)
                VALUES(@PriceChannelId,@ScopeType,@ProductId,@ProductCategoryId,@ProductBrandId);
                """,
                [Q("@PriceChannelId", exclusion.PriceChannelId),Q("@ScopeType", exclusion.ScopeType),
                 Q("@ProductId", exclusion.ProductId),Q("@ProductCategoryId", exclusion.ProductCategoryId),
                 Q("@ProductBrandId", exclusion.ProductBrandId)], ct);
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
        foreach (var promotion in snapshot.Promotions ?? [])
            await ExecutePricingAsync(connection, transaction, """
                INSERT INTO PosPromotions(PromotionId,Priority,CreatedAtUtc,Payload)
                VALUES(@PromotionId,@Priority,@CreatedAtUtc,@Payload);
                """,
                [Q("@PromotionId", promotion.PromotionId), Q("@Priority", promotion.Priority),
                 Q("@CreatedAtUtc", promotion.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                 Q("@Payload", JsonSerializer.Serialize(promotion))], ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<PosResolvedPrice> ResolvePriceAsync(
        Guid productId,
        Guid? customerId,
        decimal quantity,
        CancellationToken ct = default)
    {
        var key = productId.ToString("D");
        var result = await ResolvePricesAsync([new(key, productId, quantity)], customerId, ct);
        return result[key];
    }

    public async Task<IReadOnlyDictionary<string, PosResolvedPrice>> ResolvePricesAsync(
        IReadOnlyCollection<PosPriceLineRequest> requests,
        Guid? customerId,
        CancellationToken ct = default)
    {
        if (requests.Count == 0) return new Dictionary<string, PosResolvedPrice>();
        if (requests.Any(request => request.Quantity <= 0))
            throw new ArgumentOutOfRangeException(nameof(requests));
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        Guid? channelId = null;
        if (customerId is not null)
        {
            await using var customer = connection.CreateCommand();
            customer.CommandText = "SELECT PriceChannelId FROM PosPricingCustomers WHERE CustomerId=@CustomerId AND IsActive=1;";
            customer.Parameters.Add(Q("@CustomerId", customerId));
            var value = await customer.ExecuteScalarAsync(ct);
            channelId = value is null or DBNull ? null : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        var snapshot = await ReadPricingSnapshotAsync(ct);
        var channelRules = snapshot.PriceChannels.Select(value =>
            new PriceChannelRule(value.PriceChannelId,value.Strategy,value.Value)).ToArray();
        var tierRules = snapshot.PriceChannelTiers.Select(value =>
            new PriceChannelTierRule(value.PriceChannelId,value.ProductId,value.MinimumQuantity,
                value.Amount,value.CurrencyCode)).ToArray();
        var exclusionRules = snapshot.PriceChannelExclusions.Select(value =>
            new PriceChannelExclusionRule(value.PriceChannelId,value.ProductId,
                value.ProductCategoryId,value.ProductBrandId)).ToArray();
        var quantities = requests.GroupBy(value => value.ProductId)
            .ToDictionary(group => group.Key,group => group.Sum(value => value.Quantity));
        var inputs = new List<PromotionPriceLineInput>(requests.Count);
        foreach (var request in requests)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Name,CategoryName,UnitPrice,CurrencyCode,ProductCategoryId,ProductBrandId,
                       ProductCategoryAncestorIds,AverageUnitCost,LatestUnitCost,TargetMarginPercent
                FROM PosCatalogProducts WHERE ProductId=@ProductId AND IsActive=1;
                """;
            command.Parameters.Add(Q("@ProductId", request.ProductId));
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new KeyNotFoundException("The product is not available in the local catalog.");
            var name = reader.GetString(0);
            var category = reader.IsDBNull(1) ? null : reader.GetString(1);
            var baseAmount = Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture);
            var currency = reader.GetString(3);
            var productContext = new PriceChannelProductContext(
                request.ProductId,
                reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? [] : JsonSerializer.Deserialize<Guid[]>(reader.GetString(6)) ?? [],
                currency,
                Convert.ToDecimal(reader.GetValue(7),CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(8),CultureInfo.InvariantCulture),
                reader.IsDBNull(9) ? null : Convert.ToDecimal(reader.GetValue(9),CultureInfo.InvariantCulture));
            await reader.DisposeAsync();
            var pricingQuantity = quantities[request.ProductId];
            var channel = PriceChannelResolver.Resolve(
                channelId,baseAmount,pricingQuantity,productContext,
                channelRules,tierRules,exclusionRules);
            inputs.Add(new(request.Key, PromotionItemType.Product, request.ProductId, null,
                name, category, baseAmount, channel.Amount, request.Quantity, currency,
                channel.PriceChannelId,
                EligibleForPromotion: request.EligibleForPromotion));
        }
        var now = Clock.GetUtcNow();
        var resolved = PromotionPriceResolver.Resolve(
            inputs, (snapshot.Promotions ?? [])
                .Where(promotion => promotion.StartsAtUtc is null || promotion.StartsAtUtc <= now)
                .Where(promotion => promotion.EndsAtUtc is null || promotion.EndsAtUtc >= now)
                .Select(ToRule).ToArray(),
            snapshot.AllowPromotionChannelCombination);
        return resolved.Lines.ToDictionary(line => line.Input.Key, line => new PosResolvedPrice(
            line.Input.ProductId!.Value, line.Input.BaseUnitPrice, line.EffectiveUnitPrice,
            line.Input.CurrencyCode, line.PriceSource, line.PriceChannelId,
            line.DiscountAmount, line.Adjustments.Select(value => value.PromotionId).Distinct().ToArray(),
            line.ReferenceUnitPrice),
            StringComparer.OrdinalIgnoreCase);
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
            CREATE TABLE IF NOT EXISTS PosPriceChannels(
              PriceChannelId TEXT PRIMARY KEY,Code TEXT NOT NULL,Name TEXT NOT NULL,
              Strategy TEXT NOT NULL,Value TEXT NULL);
            CREATE TABLE IF NOT EXISTS PosPriceChannelTiers(
              PriceChannelId TEXT NOT NULL,ProductId TEXT NOT NULL,MinimumQuantity TEXT NOT NULL,
              Amount TEXT NOT NULL,CurrencyCode TEXT NOT NULL,
              PRIMARY KEY(PriceChannelId,ProductId,MinimumQuantity),
              FOREIGN KEY(PriceChannelId) REFERENCES PosPriceChannels(PriceChannelId) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS PosPriceChannelExclusions(
              PriceChannelId TEXT NOT NULL,ScopeType TEXT NOT NULL,ProductId TEXT NULL,
              ProductCategoryId TEXT NULL,ProductBrandId TEXT NULL,
              PRIMARY KEY(PriceChannelId,ScopeType,ProductId,ProductCategoryId,ProductBrandId),
              FOREIGN KEY(PriceChannelId) REFERENCES PosPriceChannels(PriceChannelId) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS PosWithholdingRules(
              RuleId TEXT NOT NULL,Version INTEGER NOT NULL,Code TEXT NOT NULL,Name TEXT NOT NULL,
              Kind TEXT NOT NULL,Direction TEXT NOT NULL,Moment TEXT NOT NULL,BaseKind TEXT NOT NULL,
              ConceptCode TEXT NULL,JurisdictionCode TEXT NULL,Rate TEXT NOT NULL,MinimumBase TEXT NOT NULL,
              RequiredResponsibilities TEXT NOT NULL,EffectiveFrom TEXT NOT NULL,EffectiveTo TEXT NULL,
              IsActive INTEGER NOT NULL,PRIMARY KEY(RuleId,Version));
            CREATE TABLE IF NOT EXISTS PosPricingConfiguration(
              ConfigurationId INTEGER PRIMARY KEY CHECK(ConfigurationId=1),
              AllowPromotionChannelCombination INTEGER NOT NULL);
            INSERT OR IGNORE INTO PosPricingConfiguration(ConfigurationId,AllowPromotionChannelCombination)
              VALUES(1,0);
            CREATE TABLE IF NOT EXISTS PosPromotions(
              PromotionId TEXT PRIMARY KEY,Priority INTEGER NOT NULL,
              CreatedAtUtc TEXT NOT NULL,Payload TEXT NOT NULL);
            DROP TABLE IF EXISTS PosPriceChannelItems;
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

    private static PromotionRule ToRule(PosPromotion promotion) => new(
        promotion.PromotionId, promotion.Name, promotion.Priority, promotion.IsCombinable,
        promotion.CouponCode, promotion.CreatedAtUtc.UtcDateTime,
        promotion.Conditions.Select(condition => new PromotionConditionRule(
            (PromotionItemType)condition.ItemType, condition.ProductId, condition.ServiceId,
            condition.CategoryName, condition.MinimumQuantity, condition.MinimumSubtotal)).ToArray(),
        promotion.Benefits.Select(benefit => new PromotionBenefitRule(
            (PromotionBenefitType)benefit.BenefitType, (PromotionItemType)benefit.TargetItemType,
            benefit.ProductId, benefit.ServiceId, benefit.CategoryName,
            benefit.DiscountPercentage, benefit.DiscountAmount, benefit.FixedUnitPrice,
            benefit.AppliesToQuantity)).ToArray());
}
