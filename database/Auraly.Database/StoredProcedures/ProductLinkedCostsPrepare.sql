CREATE PROCEDURE [dbo].[ProductLinkedCostsPrepare]
    @BusinessId UNIQUEIDENTIFIER,
    @ParentProductId UNIQUEIDENTIFIER = NULL,
    @ParentCost DECIMAL(19,6) = NULL,
    @ChildProductId UNIQUEIDENTIFIER = NULL,
    @UserId UNIQUEIDENTIFIER,
    @Now DATETIMEOFFSET(7),
    @FamiliesJson NVARCHAR(MAX) = NULL,
    @PrepareParents BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TenantId UNIQUEIDENTIFIER,
            @SharesPrices BIT,
            @InventoryCostBasis NVARCHAR(32);

    SELECT @TenantId=business.TenantId,@SharesPrices=business.SharesProductPrices,
      @InventoryCostBasis=tenant.InventoryCostBasis
    FROM dbo.Businesses business WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId
    WHERE business.BusinessId=@BusinessId;

    IF @TenantId IS NULL
        THROW 51020,'El negocio no existe en el alcance autenticado.',1;

    -- Legacy scalar callers and receipt batches converge on this same owner.
    DECLARE @Families TABLE(Ordinal int PRIMARY KEY,ParentProductId uniqueidentifier,ParentCost decimal(19,6),ChildProductId uniqueidentifier,
      SourceDocumentId uniqueidentifier,SourceLineNumber int);
    IF @FamiliesJson IS NULL
        INSERT @Families VALUES(0,@ParentProductId,@ParentCost,@ChildProductId,NULL,NULL);
    ELSE
        INSERT @Families SELECT CONVERT(int,input.[key]),family.ParentProductId,family.ParentCost,family.ChildProductId,
          family.SourceDocumentId,family.SourceLineNumber
        FROM OPENJSON(@FamiliesJson) input
        CROSS APPLY OPENJSON(input.value) WITH(ParentProductId uniqueidentifier,ParentCost decimal(19,6),ChildProductId uniqueidentifier,
          SourceDocumentId uniqueidentifier,SourceLineNumber int) family;
    IF EXISTS(SELECT 1 FROM @Families WHERE ParentProductId IS NULL)
        THROW 51020,'La preparación requiere un producto principal válido.',1;
    IF EXISTS(SELECT 1 FROM @Families WHERE
        (SourceDocumentId IS NULL AND SourceLineNumber IS NOT NULL)
        OR (SourceDocumentId IS NOT NULL AND (SourceLineNumber IS NULL OR SourceLineNumber<=0)))
        THROW 51020,'La preparación requiere una referencia de origen completa.',1;
    IF EXISTS(SELECT 1 FROM @Families family WHERE family.SourceDocumentId IS NOT NULL
      AND NOT EXISTS(SELECT 1 FROM dbo.GoodsReceipts receipt
        JOIN dbo.GoodsReceiptLines line ON line.GoodsReceiptId=receipt.GoodsReceiptId
        WHERE receipt.BusinessId=@BusinessId
          AND receipt.GoodsReceiptId=family.SourceDocumentId AND line.LineNumber=family.SourceLineNumber
          AND line.ProductId=COALESCE(family.ChildProductId,family.ParentProductId)))
        THROW 51020,'La partida de origen no pertenece a la recepción y producto del negocio.',1;
    DECLARE @Parents TABLE(Ordinal int PRIMARY KEY,ParentProductId uniqueidentifier,ResolvedParentCost decimal(19,6));
    INSERT @Parents
    SELECT requested.Ordinal,requested.ParentProductId,COALESCE(requested.ParentCost,previous.ParentCost,preparation.CostBasisAmount,
                                            latest.LatestUnitCost,
                                            price.CostBasisAmount)
        FROM @Families requested
        OUTER APPLY(SELECT TOP(1) prior.ParentCost FROM @Families prior
          WHERE prior.ParentProductId=requested.ParentProductId AND prior.Ordinal<requested.Ordinal
            AND prior.ParentCost IS NOT NULL ORDER BY prior.Ordinal DESC) previous
        OUTER APPLY (
            SELECT TOP(1) value.CostBasisAmount
            FROM dbo.ProductPricePreparations value
            WHERE value.BusinessId=@BusinessId
              AND value.ProductId=requested.ParentProductId
              AND value.Status=N'Pending'
            ORDER BY value.PreparedAt DESC,value.ProductPricePreparationId DESC
        ) preparation
        OUTER APPLY (
            SELECT TOP(1) value.LatestUnitCost
            FROM dbo.SupplierProductLatestCosts value
            WHERE value.BusinessId=@BusinessId
              AND value.ProductId=requested.ParentProductId
            ORDER BY value.ObservedAt DESC,value.SupplierId
        ) latest
        OUTER APPLY (
            SELECT TOP(1) value.CostBasisAmount
            FROM dbo.ProductPrices value
            WHERE value.BusinessId=@BusinessId
              AND value.ProductId=requested.ParentProductId
              AND value.IsActive=1
            ORDER BY value.ValidFrom DESC,value.ProductPriceId
        ) price
        WHERE @PrepareParents=1 OR EXISTS(SELECT 1 FROM dbo.ProductLinks link WITH(UPDLOCK,HOLDLOCK)
          WHERE link.TenantId=@TenantId AND link.ParentProductId=requested.ParentProductId
            AND link.SharesPrice=1 AND link.IsActive=1
            AND (requested.ChildProductId IS NULL OR requested.ChildProductId=link.ChildProductId))
        OPTION(RECOMPILE);

    IF EXISTS(SELECT 1 FROM @Parents WHERE ResolvedParentCost IS NULL OR ResolvedParentCost<=0)
        THROW 51020,'El producto principal necesita un costo válido antes de vincular costos.',1;

    DECLARE @Links TABLE(
        Ordinal INT NOT NULL,
        ProductId UNIQUEIDENTIFIER NOT NULL,
        ParentProductId UNIQUEIDENTIFIER NOT NULL,
        ParentCost DECIMAL(19,6) NOT NULL,
        SourceDocumentId UNIQUEIDENTIFIER NULL,
        SourceLineNumber INT NULL,
        CostFactor DECIMAL(19,6) NOT NULL,
        PRIMARY KEY(Ordinal,ProductId));

    INSERT @Links(Ordinal,ProductId,ParentProductId,ParentCost,SourceDocumentId,SourceLineNumber,CostFactor)
    SELECT parent.Ordinal,link.ChildProductId,link.ParentProductId,parent.ResolvedParentCost,
      family.SourceDocumentId,family.SourceLineNumber,link.PriceFactor
    FROM dbo.ProductLinks link WITH(UPDLOCK,HOLDLOCK)
    JOIN @Parents parent ON parent.ParentProductId=link.ParentProductId
    JOIN @Families family ON family.Ordinal=parent.Ordinal
    INNER JOIN dbo.Products child ON child.ProductId=link.ChildProductId
                                  AND child.TenantId=@TenantId
                                  AND child.IsActive=1
    WHERE link.TenantId=@TenantId
      AND link.SharesPrice=1 AND link.IsActive=1
      AND (@PrepareParents=1 OR family.ChildProductId IS NULL OR family.ChildProductId=link.ChildProductId)
    OPTION(RECOMPILE);

    IF @PrepareParents=1
      INSERT @Links(Ordinal,ProductId,ParentProductId,ParentCost,SourceDocumentId,SourceLineNumber,CostFactor)
      SELECT parent.Ordinal,parent.ParentProductId,parent.ParentProductId,parent.ResolvedParentCost,
        family.SourceDocumentId,family.SourceLineNumber,1
      FROM @Parents parent JOIN @Families family ON family.Ordinal=parent.Ordinal;

    DECLARE @Targets TABLE(
        Ordinal INT NOT NULL,
        BusinessId UNIQUEIDENTIFIER NOT NULL,
        ProductId UNIQUEIDENTIFIER NOT NULL,
        ParentProductId UNIQUEIDENTIFIER NOT NULL,
        SourceDocumentId UNIQUEIDENTIFIER NULL,
        SourceLineNumber INT NULL,
        PublicAmount DECIMAL(19,4) NOT NULL,
        PreparedAmount DECIMAL(19,4) NOT NULL,
        CostBasis DECIMAL(19,6) NOT NULL,
        TargetMargin DECIMAL(9,6) NOT NULL,
        EffectiveMargin DECIMAL(9,6) NULL,
        RoundingIncrement DECIMAL(19,4) NOT NULL,
        RoundingMode NVARCHAR(16) NOT NULL,
        PRIMARY KEY(Ordinal,BusinessId,ProductId));

    INSERT @Targets
    SELECT link.Ordinal,price.BusinessId,link.ProductId,link.ParentProductId,link.SourceDocumentId,link.SourceLineNumber,
           price.Amount,rounded.PreparedAmount,
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
                                   AND product.TenantId=@TenantId
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
        SELECT ROUND(link.ParentCost*link.CostFactor,6) CostBasis
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
        OR (@SharesPrices=0 AND target.BusinessId=@BusinessId)) OPTION(RECOMPILE);

    IF EXISTS(
        SELECT 1 FROM @Links link
        WHERE NOT EXISTS(
            SELECT 1 FROM @Targets target
            WHERE target.ProductId=link.ProductId AND target.Ordinal=link.Ordinal))
        THROW 51020,'El producto vinculado necesita un margen y redondeo válidos para preparar su precio.',1;

    UPDATE proposal
    SET Status=N'Superseded'
    FROM dbo.PriceRevisionProposals proposal
    INNER JOIN (SELECT DISTINCT BusinessId,ProductId FROM @Targets) target ON target.BusinessId=proposal.BusinessId
                              AND target.ProductId=proposal.ProductId
    WHERE proposal.Status IN(N'PendingReview',N'Approved');

    UPDATE preparation
    SET Status=N'Superseded',SupersededAt=@Now
    FROM dbo.ProductPricePreparations preparation
    INNER JOIN (SELECT DISTINCT BusinessId,ProductId FROM @Targets) target ON target.BusinessId=preparation.BusinessId
                              AND target.ProductId=preparation.ProductId
    WHERE preparation.Status=N'Pending';

    INSERT dbo.ProductPricePreparations
      (ProductPricePreparationId,BusinessId,ProductId,SourceProductId,SourceDocumentId,SourceLineNumber,
       PreparationOrigin,PublicAmountSnapshot,PreparedAmount,CostBasisType,
       CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,
       RoundingIncrement,RoundingMode,Status,PreparedByUserId,PreparedAt,SupersededAt)
    SELECT NEWID(),target.BusinessId,target.ProductId,target.ParentProductId,target.SourceDocumentId,target.SourceLineNumber,
           CASE WHEN @PrepareParents=1 AND target.ProductId=target.ParentProductId
             THEN N'SupplierDiscount' ELSE N'LinkedProduct' END,
           target.PublicAmount,target.PreparedAmount,
           CASE WHEN @PrepareParents=1 AND target.ProductId=target.ParentProductId
             THEN CASE WHEN @InventoryCostBasis=N'WeightedAverageCost' THEN N'WeightedAverageCost'
               ELSE N'LatestReceiptCost' END ELSE N'LinkedProduct' END,
           target.CostBasis,target.TargetMargin,target.EffectiveMargin,N'Margin',
           target.RoundingIncrement,target.RoundingMode,
           CASE WHEN target.Ordinal=last.Ordinal THEN N'Pending' ELSE N'Superseded' END,@UserId,@Now,
           CASE WHEN target.Ordinal=last.Ordinal THEN NULL ELSE @Now END
    FROM @Targets target
    JOIN (SELECT BusinessId,ProductId,MAX(Ordinal) Ordinal FROM @Targets GROUP BY BusinessId,ProductId) last
      ON last.BusinessId=target.BusinessId AND last.ProductId=target.ProductId
    OPTION(RECOMPILE);
END;
GO
