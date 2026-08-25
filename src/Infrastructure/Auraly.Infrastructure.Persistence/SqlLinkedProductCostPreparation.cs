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
                WHERE value.BusinessId=product.BusinessId AND value.ProductId=product.ProductId
                ORDER BY value.ObservedAt DESC) latest
              OUTER APPLY (
                SELECT TOP(1) value.CostBasisAmount
                FROM dbo.ProductPrices value
                WHERE value.BusinessId=product.BusinessId AND value.ProductId=product.ProductId AND value.IsActive=1
                ORDER BY value.ValidFrom DESC,value.ProductPriceId) price
              WHERE product.BusinessId=@BusinessId AND product.ProductId=@ParentProductId);
            IF @ParentCost IS NULL OR @ParentCost<=0
              THROW 51020,'El producto principal necesita un costo válido antes de vincular costos.',1;

            DECLARE @ChildPriceId UNIQUEIDENTIFIER,@Margin DECIMAL(9,6),@TaxRate DECIMAL(9,6);
            SELECT TOP(1) @ChildPriceId=price.ProductPriceId,
              @Margin=COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent),
              @TaxRate=COALESCE(tax.Rate,0)
            FROM dbo.Products product
            JOIN dbo.ProductPrices price ON price.BusinessId=product.BusinessId AND price.ProductId=product.ProductId AND price.IsActive=1
            LEFT JOIN dbo.TaxProfiles tax ON tax.BusinessId=product.BusinessId AND tax.TaxProfileId=product.TaxProfileId
            WHERE product.BusinessId=@BusinessId AND product.ProductId=@ChildProductId
            ORDER BY price.ValidFrom DESC,price.ProductPriceId;
            IF @ChildPriceId IS NULL OR @Margin IS NULL OR @Margin<0 OR @Margin>=100
              THROW 51020,'El producto vinculado necesita un margen válido para preparar su precio.',1;

            DECLARE @LinkedCost DECIMAL(19,6)=ROUND(@ParentCost*@CostFactor,6);
            DECLARE @PreparedAmount DECIMAL(19,4)=ROUND(
              (@LinkedCost/(1-(@Margin/100)))*(1+(@TaxRate/100)),4);
            UPDATE dbo.ProductPrices
            SET CostBasisType=N'LinkedProduct',CostBasisAmount=@LinkedCost,
                TargetMarginPercent=@Margin,PreparedAmount=@PreparedAmount
            WHERE ProductPriceId=@ChildPriceId AND BusinessId=@BusinessId;
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
