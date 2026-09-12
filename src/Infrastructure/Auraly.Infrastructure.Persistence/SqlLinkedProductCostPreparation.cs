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
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @ParentCost DECIMAL(19,6)=(
              SELECT COALESCE(preparation.CostBasisAmount,latest.LatestUnitCost,price.CostBasisAmount)
              FROM dbo.Products product
              OUTER APPLY (
                SELECT TOP(1) value.CostBasisAmount
                FROM dbo.ProductPricePreparations value
                WHERE value.BusinessId=@BusinessId AND value.ProductId=product.ProductId
                  AND value.Status=N'Pending'
                ORDER BY value.PreparedAt DESC,value.ProductPricePreparationId DESC) preparation
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
              OUTER APPLY (
                SELECT TOP(1) pending.TargetMarginPercent,pending.EffectiveMarginPercent
                FROM dbo.ProductPricePreparations pending
                WHERE pending.BusinessId=price.BusinessId AND pending.ProductId=price.ProductId
                  AND pending.Status=N'Pending'
                ORDER BY pending.PreparedAt DESC,pending.ProductPricePreparationId DESC) preparation
              WHERE price.ProductId=@ChildProductId AND price.IsActive=1
                AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                  OR (@SharesPrices=0 AND target.BusinessId=@BusinessId))
                AND (COALESCE(preparation.TargetMarginPercent,preparation.EffectiveMarginPercent,
                              price.TargetMarginPercent,price.EffectiveMarginPercent) IS NULL
                  OR COALESCE(preparation.TargetMarginPercent,preparation.EffectiveMarginPercent,
                              price.TargetMarginPercent,price.EffectiveMarginPercent)<0
                  OR COALESCE(preparation.TargetMarginPercent,preparation.EffectiveMarginPercent,
                              price.TargetMarginPercent,price.EffectiveMarginPercent)>=100))
              THROW 51020,'El producto vinculado necesita un margen válido para preparar su precio.',1;

            DECLARE @LinkedCost DECIMAL(19,6)=ROUND(@ParentCost*@CostFactor,6);
            DECLARE @Targets TABLE(
              BusinessId UNIQUEIDENTIFIER PRIMARY KEY,
              PublicAmount DECIMAL(19,4) NOT NULL,
              PreparedAmount DECIMAL(19,4) NOT NULL,
              TargetMargin DECIMAL(9,6) NOT NULL,
              EffectiveMargin DECIMAL(9,6) NULL,
              RoundingIncrement DECIMAL(19,4) NOT NULL,
              RoundingMode NVARCHAR(16) NOT NULL);
            INSERT @Targets
            SELECT price.BusinessId,price.Amount,calculation.PreparedAmount,
                   calculation.TargetMargin,
                   CASE WHEN calculation.NetSalePrice<=0 THEN NULL
                        ELSE ((calculation.NetSalePrice-@LinkedCost)/calculation.NetSalePrice)*100 END,
                   COALESCE(preparation.RoundingIncrement,price.RoundingIncrement,1),
                   COALESCE(preparation.RoundingMode,price.RoundingMode,N'Nearest')
            FROM dbo.ProductPrices price
            JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
            JOIN dbo.Products product ON product.ProductId=price.ProductId AND product.TenantId=@TenantId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=product.TaxProfileId
            OUTER APPLY (
              SELECT TOP(1) pending.TargetMarginPercent,pending.EffectiveMarginPercent,
                pending.RoundingIncrement,pending.RoundingMode
              FROM dbo.ProductPricePreparations pending
              WHERE pending.BusinessId=price.BusinessId AND pending.ProductId=price.ProductId
                AND pending.Status=N'Pending'
              ORDER BY pending.PreparedAt DESC,pending.ProductPricePreparationId DESC) preparation
            CROSS APPLY (
              SELECT COALESCE(preparation.TargetMarginPercent,preparation.EffectiveMarginPercent,
                              price.TargetMarginPercent,price.EffectiveMarginPercent) TargetMargin,
                     ROUND((@LinkedCost/(1-(COALESCE(preparation.TargetMarginPercent,
                       preparation.EffectiveMarginPercent,price.TargetMarginPercent,
                       price.EffectiveMarginPercent)/100)))
                       *(1+(COALESCE(tax.Rate,0)/100)),4) PreparedAmount
            ) raw
            CROSS APPLY (
              SELECT raw.TargetMargin,raw.PreparedAmount,
                     raw.PreparedAmount/(1+(COALESCE(tax.Rate,0)/100)) NetSalePrice
            ) calculation
            WHERE price.ProductId=@ChildProductId AND price.IsActive=1
              AND calculation.TargetMargin>=0 AND calculation.TargetMargin<100
              AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                OR (@SharesPrices=0 AND target.BusinessId=@BusinessId));

            UPDATE preparation SET Status=N'Superseded',SupersededAt=@Now
            FROM dbo.ProductPricePreparations preparation
            INNER JOIN @Targets target ON target.BusinessId=preparation.BusinessId
            WHERE preparation.ProductId=@ChildProductId AND preparation.Status=N'Pending';
            INSERT dbo.ProductPricePreparations
              (ProductPricePreparationId,BusinessId,ProductId,SourceProductId,
               PreparationOrigin,PublicAmountSnapshot,PreparedAmount,CostBasisType,
               CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,
               RoundingIncrement,RoundingMode,Status,PreparedByUserId,PreparedAt)
            SELECT NEWID(),target.BusinessId,@ChildProductId,@ParentProductId,N'LinkedProduct',
                   target.PublicAmount,target.PreparedAmount,N'LinkedProduct',@LinkedCost,
                   target.TargetMargin,target.EffectiveMargin,N'Margin',target.RoundingIncrement,
                   target.RoundingMode,N'Pending',@UserId,@Now
            FROM @Targets target;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ParentProductId", parentProductId);
        command.Parameters.AddWithValue("@ChildProductId", childProductId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", now);
        var factor = command.Parameters.Add("@CostFactor", SqlDbType.Decimal);
        factor.Precision = 19;
        factor.Scale = 6;
        factor.Value = costFactor;
        await command.ExecuteNonQueryAsync(ct);
    }
}
