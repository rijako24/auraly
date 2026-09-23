CREATE PROCEDURE [dbo].[ProductLinkedCostsPrepare]
    @BusinessId UNIQUEIDENTIFIER,
    @ParentProductId UNIQUEIDENTIFIER,
    @ParentCost DECIMAL(19,6) = NULL,
    @ChildProductId UNIQUEIDENTIFIER = NULL,
    @UserId UNIQUEIDENTIFIER,
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TenantId UNIQUEIDENTIFIER,
            @SharesPrices BIT,
            @ResolvedParentCost DECIMAL(19,6);

    SELECT @TenantId=TenantId,@SharesPrices=SharesProductPrices
    FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId;

    IF @TenantId IS NULL
        THROW 51020,'El negocio no existe en el alcance autenticado.',1;

    IF NOT EXISTS(
        SELECT 1
        FROM dbo.ProductLinks link WITH(UPDLOCK,HOLDLOCK)
        WHERE link.BusinessId=@BusinessId
          AND link.ParentProductId=@ParentProductId
          AND link.SharesPrice=1 AND link.IsActive=1
          AND (@ChildProductId IS NULL OR link.ChildProductId=@ChildProductId))
        RETURN;

    SET @ResolvedParentCost=@ParentCost;
    IF @ResolvedParentCost IS NULL
    BEGIN
        SELECT @ResolvedParentCost=COALESCE(preparation.CostBasisAmount,
                                            latest.LatestUnitCost,
                                            price.CostBasisAmount)
        FROM dbo.Products product
        OUTER APPLY (
            SELECT TOP(1) value.CostBasisAmount
            FROM dbo.ProductPricePreparations value
            WHERE value.BusinessId=@BusinessId
              AND value.ProductId=product.ProductId
              AND value.Status=N'Pending'
            ORDER BY value.PreparedAt DESC,value.ProductPricePreparationId DESC
        ) preparation
        OUTER APPLY (
            SELECT TOP(1) value.LatestUnitCost
            FROM dbo.SupplierProductLatestCosts value
            WHERE value.BusinessId=@BusinessId
              AND value.ProductId=product.ProductId
            ORDER BY value.ObservedAt DESC,value.SupplierId
        ) latest
        OUTER APPLY (
            SELECT TOP(1) value.CostBasisAmount
            FROM dbo.ProductPrices value
            WHERE value.BusinessId=@BusinessId
              AND value.ProductId=product.ProductId
              AND value.IsActive=1
            ORDER BY value.ValidFrom DESC,value.ProductPriceId
        ) price
        WHERE product.TenantId=@TenantId
          AND product.ProductId=@ParentProductId;
    END;

    IF @ResolvedParentCost IS NULL OR @ResolvedParentCost<=0
        THROW 51020,'El producto principal necesita un costo válido antes de vincular costos.',1;

    DECLARE @Links TABLE(
        ProductId UNIQUEIDENTIFIER PRIMARY KEY,
        CostFactor DECIMAL(19,6) NOT NULL);

    INSERT @Links(ProductId,CostFactor)
    SELECT link.ChildProductId,link.PriceFactor
    FROM dbo.ProductLinks link WITH(UPDLOCK,HOLDLOCK)
    INNER JOIN dbo.Products child ON child.ProductId=link.ChildProductId
                                  AND (child.TenantId=@TenantId
                                       OR (child.TenantId IS NULL AND child.BusinessId=@BusinessId))
                                  AND child.IsActive=1
    WHERE link.BusinessId=@BusinessId
      AND link.ParentProductId=@ParentProductId
      AND link.SharesPrice=1 AND link.IsActive=1
      AND (@ChildProductId IS NULL OR link.ChildProductId=@ChildProductId);

    DECLARE @Targets TABLE(
        BusinessId UNIQUEIDENTIFIER NOT NULL,
        ProductId UNIQUEIDENTIFIER NOT NULL,
        PublicAmount DECIMAL(19,4) NOT NULL,
        PreparedAmount DECIMAL(19,4) NOT NULL,
        CostBasis DECIMAL(19,6) NOT NULL,
        TargetMargin DECIMAL(9,6) NOT NULL,
        EffectiveMargin DECIMAL(9,6) NULL,
        RoundingIncrement DECIMAL(19,4) NOT NULL,
        RoundingMode NVARCHAR(16) NOT NULL,
        PRIMARY KEY(BusinessId,ProductId));

    INSERT @Targets
    SELECT price.BusinessId,link.ProductId,price.Amount,rounded.PreparedAmount,
           cost.CostBasis,policy.TargetMargin,
           CASE WHEN net.NetSalePrice<=0 THEN NULL
                ELSE ROUND(
                    (CAST(net.NetSalePrice-cost.CostBasis AS DECIMAL(25,12))
                     * CAST(100 AS DECIMAL(3,0)))
                    / CAST(net.NetSalePrice AS DECIMAL(25,12)),6) END,
           policy.RoundingIncrement,policy.RoundingMode
    FROM @Links link
    INNER JOIN dbo.ProductPrices price WITH(UPDLOCK,HOLDLOCK)
      ON price.ProductId=link.ProductId AND price.IsActive=1
    INNER JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
    INNER JOIN dbo.Products product ON product.ProductId=link.ProductId
                                   AND (product.TenantId=@TenantId
                                        OR (product.TenantId IS NULL AND product.BusinessId=@BusinessId))
    LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=product.TaxProfileId
    OUTER APPLY (
        SELECT TOP(1) pending.TargetMarginPercent,pending.EffectiveMarginPercent,
               pending.RoundingIncrement,pending.RoundingMode
        FROM dbo.ProductPricePreparations pending WITH(UPDLOCK,HOLDLOCK)
        WHERE pending.BusinessId=price.BusinessId
          AND pending.ProductId=price.ProductId
          AND pending.Status=N'Pending'
        ORDER BY pending.PreparedAt DESC,pending.ProductPricePreparationId DESC
    ) preparation
    CROSS APPLY (
        SELECT ROUND(@ResolvedParentCost*link.CostFactor,6) CostBasis
    ) cost
    CROSS APPLY (
        SELECT COALESCE(preparation.TargetMarginPercent,
                        preparation.EffectiveMarginPercent,
                        price.TargetMarginPercent,
                        price.EffectiveMarginPercent) TargetMargin,
               COALESCE(preparation.RoundingIncrement,price.RoundingIncrement,1) RoundingIncrement,
               COALESCE(preparation.RoundingMode,price.RoundingMode,N'Nearest') RoundingMode
    ) policy
    CROSS APPLY (
        SELECT ROUND((cost.CostBasis/(1-(policy.TargetMargin/100)))
                     *(1+(COALESCE(tax.Rate,0)/100)),4) RawAmount
    ) raw
    CROSS APPLY (
        SELECT ROUND((CASE policy.RoundingMode
                        WHEN N'Up' THEN CEILING(raw.RawAmount/policy.RoundingIncrement)
                        WHEN N'Down' THEN FLOOR(raw.RawAmount/policy.RoundingIncrement)
                        ELSE ROUND(raw.RawAmount/policy.RoundingIncrement,0)
                      END)*policy.RoundingIncrement,4) PreparedAmount
    ) rounded
    CROSS APPLY (
        SELECT rounded.PreparedAmount/(1+(COALESCE(tax.Rate,0)/100)) NetSalePrice
    ) net
    WHERE policy.TargetMargin>=0 AND policy.TargetMargin<100
      AND policy.RoundingIncrement>0
      AND policy.RoundingMode IN(N'Nearest',N'Up',N'Down')
      AND ((@SharesPrices=1 AND target.TenantId=@TenantId
            AND target.SharesProductPrices=1 AND target.IsActive=1)
        OR (@SharesPrices=0 AND target.BusinessId=@BusinessId));

    IF EXISTS(
        SELECT 1 FROM @Links link
        WHERE NOT EXISTS(
            SELECT 1 FROM @Targets target
            WHERE target.ProductId=link.ProductId))
        THROW 51020,'El producto vinculado necesita un margen y redondeo válidos para preparar su precio.',1;

    UPDATE proposal
    SET Status=N'Superseded'
    FROM dbo.PriceRevisionProposals proposal
    INNER JOIN @Targets target ON target.BusinessId=proposal.BusinessId
                              AND target.ProductId=proposal.ProductId
    WHERE proposal.Status IN(N'PendingReview',N'Approved');

    UPDATE preparation
    SET Status=N'Superseded',SupersededAt=@Now
    FROM dbo.ProductPricePreparations preparation
    INNER JOIN @Targets target ON target.BusinessId=preparation.BusinessId
                              AND target.ProductId=preparation.ProductId
    WHERE preparation.Status=N'Pending';

    INSERT dbo.ProductPricePreparations
      (ProductPricePreparationId,BusinessId,ProductId,SourceProductId,
       PreparationOrigin,PublicAmountSnapshot,PreparedAmount,CostBasisType,
       CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,
       RoundingIncrement,RoundingMode,Status,PreparedByUserId,PreparedAt)
    SELECT NEWID(),target.BusinessId,target.ProductId,@ParentProductId,
           N'LinkedProduct',target.PublicAmount,target.PreparedAmount,N'LinkedProduct',
           target.CostBasis,target.TargetMargin,target.EffectiveMargin,N'Margin',
           target.RoundingIncrement,target.RoundingMode,N'Pending',@UserId,@Now
    FROM @Targets target;
END;
GO
