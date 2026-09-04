using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Catalog;
using Auraly.Domain.Pricing;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Promotions;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed record CommercePriceRequest(string Key, Guid ProductId, decimal Quantity);
public sealed record CommercePriceResolution(
    Guid ProductId, decimal UnitPrice, string CurrencyCode, string PriceSource,
    Guid? PriceChannelId, decimal PromotionDiscount);

public sealed partial class SqlOnlineSalesDraftStore
{
    public static async Task<IReadOnlyDictionary<string, CommercePriceResolution>> ResolveCommercePricesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid warehouseId,
        Guid? customerId,
        IReadOnlyCollection<CommercePriceRequest> requests,
        CancellationToken ct)
    {
        var resolved = await ResolveProductPricesAsync(
            connection,transaction,businessId,warehouseId,customerId,
            requests.Select(value => new SalePriceRequest(
                value.Key,value.ProductId,value.Quantity)).ToArray(),ct);
        return resolved.ToDictionary(
            pair => pair.Key,
            pair => new CommercePriceResolution(
                pair.Value.Input.ProductId!.Value,pair.Value.EffectiveUnitPrice,
                pair.Value.Input.CurrencyCode,pair.Value.PriceSource,
                pair.Value.PriceChannelId,pair.Value.DiscountAmount),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyDictionary<string, PromotionPriceLineResult>> ResolveProductPricesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid warehouseId,
        Guid? customerId,
        IReadOnlyCollection<SalePriceRequest> requests,
        CancellationToken ct)
    {
        var channelConfiguration = await LoadChannelConfigurationAsync(
            connection,transaction,businessId,customerId,ct);
        var quantities = requests.GroupBy(value => value.ProductId)
            .ToDictionary(group => group.Key,group => group.Sum(value => value.Quantity));
        var inputs = new List<PromotionPriceLineInput>(requests.Count);
        foreach (var request in requests)
        {
            var product = await ReadProductAsync(
                connection, transaction, businessId, warehouseId, request.ProductId, ct);
            var totalQuantity = quantities[request.ProductId];
            var channel = PriceChannelResolver.Resolve(
                channelConfiguration.PriceChannelId,product.UnitPrice,totalQuantity,
                new PriceChannelProductContext(
                    request.ProductId,product.ProductCategoryId,product.ProductBrandId,
                    product.ProductCategoryAncestorIds,product.CurrencyCode,product.UnitCost,product.LatestUnitCost,
                    product.TargetMarginPercent),
                channelConfiguration.Channels,channelConfiguration.Tiers,
                channelConfiguration.Exclusions);
            inputs.Add(new(
                request.Key, PromotionItemType.Product, request.ProductId, null, product.Name,
                product.CategoryName, product.UnitPrice,
                channel.Amount,request.Quantity, product.CurrencyCode, channel.PriceChannelId,
                EligibleForPromotion: request.EligibleForPromotion));
        }

        var configuration = await LoadPromotionConfigurationAsync(
            connection, transaction, businessId, ct);
        var result = PromotionPriceResolver.Resolve(
            inputs, configuration.Promotions, configuration.AllowChannelCombination);
        return result.Lines.ToDictionary(line => line.Input.Key, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task RepriceDraftAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DraftState state,
        Guid draftId,
        Guid? customerId,
        CancellationToken ct)
    {
        var lines = await ReadLineProductsAsync(connection, transaction, draftId, ct);
        if (lines.Count == 0) return;
        var prices = await ResolveProductPricesAsync(
            connection, transaction, state.BusinessId, state.WarehouseId, customerId,
            lines.Select(line => new SalePriceRequest(
                line.LineId.ToString("D"), line.ProductId, line.Quantity,
                !string.Equals(line.PriceSource, "Manual", StringComparison.Ordinal))).ToArray(), ct);
        foreach (var line in lines)
        {
            var price = prices[line.LineId.ToString("D")];
            if (string.Equals(line.PriceSource, "Manual", StringComparison.Ordinal))
                continue;
            var unitPrice = decimal.Round(
                TaxExclusive(price.ReferenceUnitPrice, line.TaxRate), 2,
                MidpointRounding.AwayFromZero);
            var targetNet = decimal.Round(
                TaxExclusive(price.LineTotal, line.TaxRate), 2,
                MidpointRounding.AwayFromZero);
            var promotionDiscount = decimal.Round(
                Math.Max(0, line.Quantity * unitPrice - targetNet), 2,
                MidpointRounding.AwayFromZero);
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.SalesDraftLines
                SET BaseUnitPrice=@BaseUnitPrice,UnitPrice=@UnitPrice,CurrencyCode=@CurrencyCode,
                    PriceSource=@PriceSource,PriceChannelId=@PriceChannelId,
                    PromotionDiscountAmount=@PromotionDiscount
                WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
                """,
                [
                    P("@BaseUnitPrice", price.Input.BaseUnitPrice),
                    P("@UnitPrice", unitPrice),
                    P("@CurrencyCode", price.Input.CurrencyCode),
                    P("@PriceSource", price.PriceSource), P("@PriceChannelId", price.PriceChannelId),
                    P("@PromotionDiscount", promotionDiscount),
                    P("@DraftId", draftId), P("@LineId", line.LineId)
                ], ct);
        }
    }

    private static async Task<PromotionConfiguration> LoadPromotionConfigurationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT tenant.AllowPromotionChannelCombination
            FROM dbo.Businesses business
            JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId
            WHERE business.BusinessId=@BusinessId;

            SELECT promotion.PromotionId,promotion.Name,promotion.Priority,promotion.IsCombinable,
                   promotion.CouponCode,promotion.CreatedAt,
                   COALESCE((
                     SELECT CONVERT(INT,c.ItemType) ItemType,c.ProductId,c.ServiceId,c.CategoryName,
                            c.MinQuantity MinimumQuantity,c.MinSubtotal MinimumSubtotal
                     FROM dbo.PromotionConditions c
                     WHERE c.PromotionId=promotion.PromotionId
                     ORDER BY c.PromotionConditionId FOR JSON PATH),N'[]'),
                   COALESCE((
                     SELECT CONVERT(INT,b.BenefitType) BenefitType,CONVERT(INT,b.TargetItemType) TargetItemType,
                            b.ProductId,b.ServiceId,b.CategoryName,b.DiscountPercentage,b.DiscountAmount,
                            b.FixedUnitPrice,b.AppliesToQuantity
                     FROM dbo.PromotionBenefits b
                     WHERE b.PromotionId=promotion.PromotionId
                     ORDER BY b.PromotionBenefitId FOR JSON PATH),N'[]')
            FROM dbo.Promotions promotion
            JOIN dbo.Businesses targetBusiness ON targetBusiness.BusinessId=@BusinessId
            WHERE promotion.TenantId=targetBusiness.TenantId
              AND (promotion.AppliesToAllBusinesses=1
                   OR EXISTS(SELECT 1 FROM pricing.PromotionBusinessScopes scope
                             WHERE scope.PromotionId=promotion.PromotionId AND scope.BusinessId=@BusinessId))
              AND promotion.IsActive=1
              AND (promotion.StartsAtUtc IS NULL OR promotion.StartsAtUtc<=SYSUTCDATETIME())
              AND (promotion.EndsAtUtc IS NULL OR promotion.EndsAtUtc>=SYSUTCDATETIME())
            ORDER BY promotion.Priority DESC,promotion.CreatedAt,promotion.PromotionId;
            """;
        command.Parameters.Add(P("@BusinessId", businessId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException("El negocio no tiene una configuración de precios válida.");
        var allowCombination = reader.GetBoolean(0);
        await reader.NextResultAsync(ct);
        var promotions = new List<PromotionRule>();
        while (await reader.ReadAsync(ct))
        {
            var conditions = JsonSerializer.Deserialize<PosPromotionCondition[]>(reader.GetString(6)) ?? [];
            var benefits = JsonSerializer.Deserialize<PosPromotionBenefit[]>(reader.GetString(7)) ?? [];
            promotions.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDateTime(5),
                conditions.Select(value => new PromotionConditionRule(
                    (PromotionItemType)value.ItemType, value.ProductId, value.ServiceId,
                    value.CategoryName, value.MinimumQuantity, value.MinimumSubtotal)).ToArray(),
                benefits.Select(value => new PromotionBenefitRule(
                    (PromotionBenefitType)value.BenefitType, (PromotionItemType)value.TargetItemType,
                    value.ProductId, value.ServiceId, value.CategoryName, value.DiscountPercentage,
                    value.DiscountAmount, value.FixedUnitPrice, value.AppliesToQuantity)).ToArray()));
        }
        return new(allowCombination, promotions);
    }

    private static async Task<ChannelConfiguration> LoadChannelConfigurationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid? customerId,
        CancellationToken ct)
    {
        if (customerId is null)
            return new(null,[],[],[]);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE WHEN setting.ValidFrom<=SYSDATETIMEOFFSET()
                              AND (setting.ValidUntil IS NULL OR setting.ValidUntil>SYSDATETIMEOFFSET())
                        THEN setting.PriceChannelId END
            FROM dbo.Customers customer
            LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=customer.CustomerId
            WHERE customer.CustomerId=@CustomerId AND customer.BusinessId=@BusinessId
              AND customer.IsActive=1;

            SELECT PriceChannelId,Strategy,Value FROM dbo.PriceChannels
            WHERE BusinessId=@BusinessId AND IsActive=1;

            SELECT item.PriceChannelId,item.ProductId,item.MinimumQuantity,item.Amount,item.CurrencyCode
            FROM dbo.PriceChannelItems item
            JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=item.PriceChannelId
            WHERE channelValue.BusinessId=@BusinessId AND channelValue.IsActive=1 AND item.IsActive=1;

            SELECT exclusion.PriceChannelId,exclusion.ProductId,
                   exclusion.ProductCategoryId,exclusion.ProductBrandId
            FROM dbo.PriceChannelExclusions exclusion
            JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=exclusion.PriceChannelId
            WHERE channelValue.BusinessId=@BusinessId AND channelValue.IsActive=1;
            """;
        command.Parameters.AddRange([P("@BusinessId",businessId),P("@CustomerId",customerId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        Guid? channelId = null;
        if (await reader.ReadAsync(ct) && !reader.IsDBNull(0)) channelId=reader.GetGuid(0);
        var channels = new List<PriceChannelRule>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            channels.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2) ? null : reader.GetDecimal(2)));
        var tiers = new List<PriceChannelTierRule>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            tiers.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetDecimal(2),reader.GetDecimal(3),reader.GetString(4)));
        var exclusions = new List<PriceChannelExclusionRule>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            exclusions.Add(new(reader.GetGuid(0),reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),reader.IsDBNull(3) ? null : reader.GetGuid(3)));
        return new(channelId,channels,tiers,exclusions);
    }

    private sealed record SalePriceRequest(
        string Key, Guid ProductId, decimal Quantity, bool EligibleForPromotion = true);
    private sealed record PromotionConfiguration(
        bool AllowChannelCombination, IReadOnlyList<PromotionRule> Promotions);
    private sealed record ChannelConfiguration(
        Guid? PriceChannelId,
        IReadOnlyCollection<PriceChannelRule> Channels,
        IReadOnlyCollection<PriceChannelTierRule> Tiers,
        IReadOnlyCollection<PriceChannelExclusionRule> Exclusions);
}
