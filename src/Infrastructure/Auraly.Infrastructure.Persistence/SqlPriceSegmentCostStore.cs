using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed record PriceSegmentTierCost(decimal Amount, decimal SalesTaxRate,
    decimal? CurrentMarginPercent);

public static class SqlPriceSegmentCostStore
{
    public static async Task<Dictionary<Guid, PriceSegmentTierCost>> LoadAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid businessId,
        IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            WITH Requested AS (
              SELECT DISTINCT TRY_CONVERT(uniqueidentifier,[value]) ProductId
              FROM OPENJSON(@ProductIdsJson)
            )
            SELECT product.ProductId,
              COALESCE(CASE WHEN costLink.ProductLinkId IS NOT NULL
                THEN ROUND(parentCost.CostBasisAmount*costLink.PriceFactor,6)
                ELSE COALESCE(preparation.CostBasisAmount,latestCost.LatestUnitCost,
                  price.CostBasisAmount) END,0),
              COALESCE(tax.Rate,0),
              COALESCE(preparation.EffectiveMarginPercent,price.EffectiveMarginPercent)
            FROM Requested requested
            JOIN dbo.Businesses businessValue ON businessValue.BusinessId=@BusinessId
            JOIN dbo.Products product ON product.ProductId=requested.ProductId
              AND product.TenantId=businessValue.TenantId AND product.IsActive=1
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=product.TaxProfileId
              AND tax.TenantId=businessValue.TenantId AND tax.IsActive=1
            LEFT JOIN dbo.ProductLinks costLink ON costLink.TenantId=businessValue.TenantId
              AND costLink.ChildProductId=product.ProductId
              AND costLink.SharesPrice=1 AND costLink.IsActive=1
            OUTER APPLY (
              SELECT TOP(1) value.CostBasisAmount,value.EffectiveMarginPercent
              FROM dbo.ProductPrices value
              WHERE value.BusinessId=@BusinessId AND value.ProductId=product.ProductId
                AND value.IsActive=1 AND value.ValidFrom<=SYSDATETIMEOFFSET()
                AND (value.ValidUntil IS NULL OR value.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY value.ValidFrom DESC,value.ProductPriceId
            ) price
            OUTER APPLY (
              SELECT TOP(1) value.CostBasisAmount,value.EffectiveMarginPercent
              FROM dbo.ProductPricePreparations value
              WHERE value.BusinessId=@BusinessId AND value.ProductId=product.ProductId
                AND value.Status=N'Pending'
              ORDER BY value.PreparedAt DESC,value.ProductPricePreparationId DESC
            ) preparation
            OUTER APPLY (
              SELECT TOP(1) latest.LatestUnitCost
              FROM dbo.SupplierProductLatestCosts latest
              LEFT JOIN dbo.SupplierProducts association
                ON association.TenantId=businessValue.TenantId
                AND association.SupplierId=latest.SupplierId
                AND association.ProductId=latest.ProductId
              WHERE latest.ProductId=product.ProductId
                AND (businessValue.SharesProductPrices=1 AND latest.BusinessId IN (
                       SELECT shared.BusinessId FROM dbo.Businesses shared
                       WHERE shared.TenantId=businessValue.TenantId
                         AND shared.SharesProductPrices=1 AND shared.IsActive=1)
                     OR businessValue.SharesProductPrices=0
                       AND latest.BusinessId=@BusinessId)
              ORDER BY CASE WHEN association.IsPrimary=1 AND association.IsActive=1
                THEN 0 ELSE 1 END,latest.ObservedAt DESC,latest.SupplierId
            ) latestCost
            OUTER APPLY (
              SELECT COALESCE(parentPreparation.CostBasisAmount,
                parentLatest.LatestUnitCost,parentPrice.CostBasisAmount) CostBasisAmount
              FROM (SELECT 1 AS Anchor) source
              OUTER APPLY (
                SELECT TOP(1) value.CostBasisAmount
                FROM dbo.ProductPricePreparations value
                WHERE value.BusinessId=@BusinessId
                  AND value.ProductId=costLink.ParentProductId
                  AND value.Status=N'Pending'
                ORDER BY value.PreparedAt DESC,value.ProductPricePreparationId DESC
              ) parentPreparation
              OUTER APPLY (
                SELECT TOP(1) value.LatestUnitCost
                FROM dbo.SupplierProductLatestCosts value
                WHERE value.BusinessId=@BusinessId
                  AND value.ProductId=costLink.ParentProductId
                ORDER BY value.ObservedAt DESC,value.SupplierId
              ) parentLatest
              OUTER APPLY (
                SELECT TOP(1) value.CostBasisAmount
                FROM dbo.ProductPrices value
                WHERE value.BusinessId=@BusinessId
                  AND value.ProductId=costLink.ParentProductId AND value.IsActive=1
                ORDER BY value.ValidFrom DESC,value.ProductPriceId
              ) parentPrice
            ) parentCost;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ProductIdsJson",
            JsonSerializer.Serialize(productIds));
        var costs = new Dictionary<Guid, PriceSegmentTierCost>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                costs.Add(reader.GetGuid(0), new(reader.GetDecimal(1), reader.GetDecimal(2),
                    reader.IsDBNull(3) ? null : reader.GetDecimal(3)));
        return costs;
    }
}
