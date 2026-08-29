using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlLinkedProductCostPreparation
{
    public static async Task PrepareAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid parentProductId,
        Guid childProductId,
        decimal costFactor,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @ParentCost DECIMAL(19,6)=(
              SELECT COALESCE(latest.LatestUnitCost,price.CostBasisAmount)
              FROM dbo.Products product
              OUTER APPLY (
                SELECT TOP(1) value.LatestUnitCost
                FROM dbo.SupplierProductLatestCosts value
                WHERE value.BusinessId=@BusinessId AND value.ProductId=product.ProductId
                ORDER BY value.ObservedAt DESC) latest
              OUTER APPLY (
                SELECT TOP(1) value.CostBasisAmount
                FROM dbo.ProductPrices value
                WHERE value.BusinessId=@BusinessId AND value.ProductId=product.ProductId AND value.IsActive=1
                ORDER BY value.ValidFrom DESC,value.ProductPriceId) price
              WHERE product.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                AND product.ProductId=@ParentProductId);
            IF @ParentCost IS NULL OR @ParentCost<=0
              THROW 51020,'El producto principal necesita un costo válido antes de vincular costos.',1;

            DECLARE @TenantId UNIQUEIDENTIFIER,@SharesPrices BIT;
            SELECT @TenantId=TenantId,@SharesPrices=SharesProductPrices
            FROM dbo.Businesses WHERE BusinessId=@BusinessId;
            IF NOT EXISTS(
              SELECT 1 FROM dbo.ProductPrices price
              JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
              WHERE price.ProductId=@ChildProductId AND price.IsActive=1
                AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                  OR (@SharesPrices=0 AND target.BusinessId=@BusinessId)))
              THROW 51020,'El producto vinculado necesita un margen válido para preparar su precio.',1;
            IF EXISTS(
              SELECT 1 FROM dbo.ProductPrices price
              JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
              WHERE price.ProductId=@ChildProductId AND price.IsActive=1
                AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                  OR (@SharesPrices=0 AND target.BusinessId=@BusinessId))
                AND (COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent) IS NULL
                  OR COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)<0
                  OR COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)>=100))
              THROW 51020,'El producto vinculado necesita un margen válido para preparar su precio.',1;

            DECLARE @LinkedCost DECIMAL(19,6)=ROUND(@ParentCost*@CostFactor,6);
            UPDATE price
            SET CostBasisType=N'LinkedProduct',CostBasisAmount=@LinkedCost,
                TargetMarginPercent=COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent),
                PreparedAmount=ROUND((@LinkedCost/(1-(COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)/100)))
                  *(1+(COALESCE(tax.Rate,0)/100)),4)
            FROM dbo.ProductPrices price
            JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
            JOIN dbo.Products product ON product.ProductId=price.ProductId AND product.TenantId=@TenantId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=product.TaxProfileId
            WHERE price.ProductId=@ChildProductId AND price.IsActive=1
              AND COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)>=0
              AND COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)<100
              AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                OR (@SharesPrices=0 AND target.BusinessId=@BusinessId));
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ParentProductId", parentProductId);
        command.Parameters.AddWithValue("@ChildProductId", childProductId);
        var factor = command.Parameters.Add("@CostFactor", SqlDbType.Decimal);
        factor.Precision = 19;
        factor.Scale = 6;
        factor.Value = costFactor;
        await command.ExecuteNonQueryAsync(ct);
    }
}
