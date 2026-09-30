using System.Data;
using System.Text.Json;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCatalogStore(SqlServerConnectionFactory connections, IAuralyIdGenerator ids) : ICatalogStore
{
    public Task<ProductDetail> CreateAsync(
        CatalogUserIdentity user, Guid productId, SaveProductRequest request,
        DateTimeOffset now, CancellationToken ct) =>
        SaveAsync(user, productId, request, now, create: true, ct);

    public Task<ProductDetail> UpdateAsync(
        CatalogUserIdentity user, Guid productId, SaveProductRequest request,
        DateTimeOffset now, CancellationToken ct) =>
        SaveAsync(user, productId, request, now, create: false, ct);

    private async Task<ProductDetail> SaveAsync(
        CatalogUserIdentity user, Guid productId, SaveProductRequest request,
        DateTimeOffset now, bool create, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var wasGenericProduct = !create && await ReadIsGenericProductAsync(
                connection, transaction, user.TenantId, productId, ct);
            await EnsureBarcodesAvailableAsync(connection, transaction, user.TenantId, productId, request.Barcodes.Select(value => value.Value), ct);
            if (create)
                request = request with { ProductCode = await NextProductCodeAsync(connection, transaction, user.TenantId, ct) };
            if (!create && !wasGenericProduct && !request.IsGenericProduct)
                await EnsurePriceUnchangedAsync(connection, transaction, user.BusinessId, productId, request.Prices.Single().Amount, ct);
            await ExecuteAsync(connection, transaction, """
                IF NOT EXISTS (
                  SELECT 1 FROM dbo.TaxProfiles t
                  WHERE t.TaxProfileId=@TaxProfileId AND t.TenantId=@TenantId AND t.IsActive=1)
                  THROW 51021, 'The sales VAT profile is outside the authenticated scope or inactive.', 1;
                IF @IsGenericProduct=0 AND NOT EXISTS (
                  SELECT 1 FROM dbo.TaxProfiles t
                  WHERE t.TaxProfileId=@PurchaseTaxProfileId AND t.TenantId=@TenantId AND t.IsActive=1)
                  THROW 51021, 'The purchase VAT profile is outside the authenticated scope or inactive.', 1;
                IF @IsGenericProduct=0 AND EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@PurchaseTaxProfileId AND TenantId=@TenantId AND Rate=0) AND @PurchaseTaxTreatment<>N'NotApplicable'
                  THROW 51024, 'A zero-rated purchase VAT profile must use NotApplicable treatment.', 1;
                IF @IsGenericProduct=0 AND EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@PurchaseTaxProfileId AND TenantId=@TenantId AND Rate>0) AND @PurchaseTaxTreatment=N'NotApplicable'
                  THROW 51024, 'A positive purchase VAT profile must use DeductibleInputVat or CapitalizedCost treatment.', 1;
                IF NOT EXISTS (SELECT 1 FROM dbo.ProductUnits WHERE TenantId=@TenantId AND Code=@BaseUnitCode AND IsActive=1)
                  THROW 51021, 'The product unit is outside the authenticated scope or inactive.', 1;
                IF @ProductCategoryId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ProductCategories WHERE TenantId=@TenantId AND ProductCategoryId=@ProductCategoryId AND IsActive=1)
                  THROW 51021, 'The product category is outside the authenticated scope or inactive.', 1;
                IF @ProductBrandId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ProductBrands WHERE TenantId=@TenantId AND ProductBrandId=@ProductBrandId AND IsActive=1)
                  THROW 51021, 'The product brand is outside the authenticated scope or inactive.', 1;
                IF @ParentProductId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Products WHERE TenantId=@TenantId AND ProductId=@ParentProductId AND IsActive=1)
                  THROW 51021, 'The linked parent product is outside the authenticated scope or inactive.', 1;
                IF @ParentProductId=@ProductId
                  THROW 51022, 'A product cannot be linked to itself.', 1;
                IF @ParentProductId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.ProductLinks WHERE TenantId=@TenantId AND ChildProductId=@ParentProductId AND IsActive=1)
                  THROW 51022, 'Linked product chains are not allowed.', 1;
                IF @ParentProductId IS NOT NULL AND @SharesInventory=1 AND EXISTS (
                  SELECT 1 FROM dbo.InventoryBalances balance JOIN dbo.Businesses businessValue ON businessValue.BusinessId=balance.BusinessId
                  WHERE businessValue.TenantId=@TenantId AND balance.ProductId=@ProductId AND balance.QuantityOnHand<>0)
                  THROW 51024, 'El producto tiene existencias. Deja su inventario en cero antes de vincularlo.', 1;
                IF @ManageInventory=0 AND EXISTS (
                  SELECT 1 FROM dbo.ProductLinks WHERE TenantId=@TenantId AND ParentProductId=@ProductId
                    AND IsActive=1 AND AllowsConversion=1)
                  THROW 51024, 'A conversion family root must manage inventory.', 1;
                IF @AllowsConversion=1 AND NOT EXISTS (
                  SELECT 1 FROM dbo.Products WHERE TenantId=@TenantId AND ProductId=@ParentProductId
                    AND IsActive=1 AND ManageStock=1 AND ConversionMaximumLossPercent IS NOT NULL)
                  THROW 51024, 'The linked parent must manage inventory and define a maximum conversion loss.', 1;
                """, ProductParameters(user, productId, request, now), ct);
            await ExecuteAsync(connection, transaction, create
                ? """
                  INSERT dbo.Products
                    (ProductId,TenantId,ProductCode,Reference,Sku,Name,Description,ProductCategoryId,CategoryName,ProductBrandId,BaseUnitCode,TaxProfileId,
                     PurchaseTaxProfileId,PurchaseTaxTreatment,ManageStock,IsGenericProduct,UnitGrossWeightKg,ConversionMaximumLossPercent,AllowsFractionalSale,IsWeighable,IsActive,Source,Currency,CreatedAt,UpdatedAt,CreatedByUserId,UpdatedByUserId)
                  VALUES
                    (@ProductId,@TenantId,@ProductCode,@Reference,@Reference,@Name,@Description,@ProductCategoryId,(SELECT Name FROM dbo.ProductCategories WHERE ProductCategoryId=@ProductCategoryId),@ProductBrandId,@BaseUnitCode,@TaxProfileId,
                     @PurchaseTaxProfileId,@PurchaseTaxTreatment,@ManageInventory,@IsGenericProduct,@UnitGrossWeightKg,@ConversionMaximumLossPercent,@AllowsFractionalSale,@IsWeighable,1,0,N'COP',@Now,NULL,@UserId,NULL);
                  """
                : """
                  UPDATE dbo.Products SET ProductCode=@ProductCode,Reference=@Reference,Sku=@Reference,Name=@Name,
                    Description=@Description,ProductCategoryId=@ProductCategoryId,CategoryName=(SELECT Name FROM dbo.ProductCategories WHERE ProductCategoryId=@ProductCategoryId),ProductBrandId=@ProductBrandId,BaseUnitCode=@BaseUnitCode,TaxProfileId=@TaxProfileId,
                    PurchaseTaxProfileId=@PurchaseTaxProfileId,PurchaseTaxTreatment=@PurchaseTaxTreatment,ManageStock=@ManageInventory,IsGenericProduct=@IsGenericProduct,UnitGrossWeightKg=@UnitGrossWeightKg,ConversionMaximumLossPercent=@ConversionMaximumLossPercent,AllowsFractionalSale=@AllowsFractionalSale,IsWeighable=@IsWeighable,UpdatedAt=@Now,UpdatedByUserId=@UserId
                  WHERE ProductId=@ProductId AND TenantId=@TenantId;
                  IF @@ROWCOUNT=0 THROW 51010, 'Product was not found in the authenticated scope.', 1;
                  IF @IsGenericProduct=1
                  BEGIN
                    UPDATE dbo.SupplierProducts SET IsPrimary=0,IsActive=0
                    WHERE TenantId=@TenantId AND ProductId=@ProductId AND IsActive=1;
                    UPDATE agreement SET IsActive=0,ValidUntil=@Now
                    FROM dbo.SupplierCostAgreements agreement
                    JOIN dbo.SupplierProducts supplierProduct ON supplierProduct.SupplierProductId=agreement.SupplierProductId
                    WHERE supplierProduct.TenantId=@TenantId AND supplierProduct.ProductId=@ProductId AND agreement.IsActive=1;
                  END;
                  DELETE FROM dbo.ProductBarcodes WHERE ProductId=@ProductId;
                  DELETE FROM dbo.ProductIdentifiers WHERE ProductId=@ProductId;
                  DELETE FROM dbo.ProductScaleConfigurations WHERE ProductId=@ProductId;
                  """, ProductParameters(user, productId, request, now), ct);
            if (create)
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.InventoryBalances
                      (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                       InventoryValue,LastProcessingSequence,UpdatedAt)
                    SELECT warehouse.BusinessId,warehouse.WarehouseId,@ProductId,0,@InitialUnitCost,0,
                           COALESCE((SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=warehouse.BusinessId),0),@Now
                    FROM dbo.Warehouses warehouse
                    INNER JOIN dbo.Businesses business ON business.BusinessId=warehouse.BusinessId
                    WHERE business.TenantId=@TenantId;
                    """, [P("@TenantId", user.TenantId), P("@ProductId", productId),
                        P("@InitialUnitCost", request.IsGenericProduct ? 0m : request.Prices.Single().CostBasisAmount),
                        P("@Now", now)], ct);
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.ProductLinks SET IsActive=0,UpdatedAt=@Now WHERE TenantId=@TenantId AND ChildProductId=@ProductId AND IsActive=1;
                IF @ParentProductId IS NOT NULL
                BEGIN
                  IF EXISTS(SELECT 1 FROM dbo.ProductLinks WHERE TenantId=@TenantId AND ChildProductId=@ProductId)
                    UPDATE dbo.ProductLinks SET ParentProductId=@ParentProductId,InventoryFactor=@InventoryFactor,
                      PriceFactor=@PriceFactor,ConversionFactor=@ConversionFactor,SharesInventory=@SharesInventory,
                      SharesPrice=@SharesPrice,AllowsConversion=@AllowsConversion,IsActive=1,UpdatedAt=@Now
                    WHERE TenantId=@TenantId AND ChildProductId=@ProductId;
                  ELSE
                    INSERT dbo.ProductLinks(ProductLinkId,TenantId,ChildProductId,ParentProductId,InventoryFactor,PriceFactor,ConversionFactor,SharesInventory,SharesPrice,AllowsConversion,IsActive,CreatedAt)
                    VALUES(@ProductLinkId,@TenantId,@ProductId,@ParentProductId,@InventoryFactor,@PriceFactor,@ConversionFactor,@SharesInventory,@SharesPrice,@AllowsConversion,1,@Now);
                END;
                """, [P("@ProductLinkId", ids.NewId()), P("@TenantId", user.TenantId), P("@ProductId", productId), P("@ParentProductId", request.Link?.ParentProductId),
                P("@InventoryFactor", request.Link is { SharesInventory: true } ? request.Link.InventoryFactor : null), P("@PriceFactor", request.Link is { SharesPrice: true } ? request.Link.PriceFactor : null),
                P("@ConversionFactor", request.Link is { AllowsConversion: true } ? request.Link.ConversionFactor : null), P("@SharesInventory", request.Link?.SharesInventory ?? false),
                P("@SharesPrice", request.Link?.SharesPrice ?? false), P("@AllowsConversion", request.Link?.AllowsConversion ?? false), P("@Now", now)], ct);


            await ExecuteAsync(connection, transaction, """
                INSERT dbo.ProductBarcodes
                  (ProductBarcodeId,TenantId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
                SELECT NEWID(),@TenantId,@ProductId,barcode.Value,barcode.IsPrimary,1,@Now
                FROM OPENJSON(@Barcodes) WITH (Value NVARCHAR(64) '$.Value',IsPrimary BIT '$.IsPrimary') barcode;
                INSERT dbo.ProductIdentifiers
                  (ProductIdentifierId,TenantId,ProductId,IdentifierType,Value,IsActive,CreatedAt)
                SELECT NEWID(),@TenantId,@ProductId,identifier.IdentifierType,identifier.Value,1,@Now
                FROM OPENJSON(@Identifiers) WITH (IdentifierType NVARCHAR(64) '$.Type',Value NVARCHAR(200) '$.Value') identifier;
                """, [P("@TenantId", user.TenantId), P("@ProductId", productId),
                    P("@Barcodes", JsonSerializer.Serialize(request.Barcodes
                        .DistinctBy(value => value.Value, StringComparer.OrdinalIgnoreCase)
                        .Select(value => new { Value = value.Value.Trim(), value.IsPrimary }))),
                    P("@Identifiers", JsonSerializer.Serialize(request.Identifiers.Select(value =>
                        new { Type = value.Type.Trim(), Value = value.Value.Trim() }))),
                    P("@Now", now)], ct);
            if (create)
            {
            foreach (var price in request.Prices)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.ProductPrices
                      (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,CostBasisType,
                       CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,
                       RoundingMode,PublishedAt,ValidFrom,IsActive,CreatedAt)
                    SELECT NEWID(),business.BusinessId,@ProductId,@Amount,@Amount,@Currency,N'Manual',@CostBasis,
                       @TargetMargin,@TargetMargin,N'Margin',1,N'Nearest',@Now,@Now,1,@Now
                    FROM dbo.Businesses business WHERE business.TenantId=@TenantId AND business.IsActive=1;
                    """, [P("@TenantId", user.TenantId), P("@ProductId", productId),
                    P("@Amount", price.Amount), P("@CostBasis", price.CostBasisAmount), P("@TargetMargin", price.TargetMarginPercent),
                    P("@Currency", price.CurrencyCode.ToUpperInvariant()), P("@Now", now)], ct);
            }
            }
            else
            {
                var price = request.Prices.Single();
                if (!wasGenericProduct && request.IsGenericProduct)
                    await ExecuteAsync(connection, transaction, """
                        UPDATE price SET Amount=0,PreparedAmount=0,
                          CostBasisType=N'Manual',CostBasisAmount=0,
                          TargetMarginPercent=0,EffectiveMarginPercent=0,
                          InputMode=N'Margin',RoundingIncrement=1,
                          RoundingMode=N'Nearest',PublishedAt=@Now
                        FROM dbo.ProductPrices price
                        INNER JOIN dbo.Businesses currentBusiness ON currentBusiness.BusinessId=@BusinessId
                        INNER JOIN dbo.Businesses targetBusiness ON targetBusiness.BusinessId=price.BusinessId
                        WHERE price.ProductId=@ProductId AND price.IsActive=1
                          AND ((currentBusiness.SharesProductPrices=1
                                AND targetBusiness.TenantId=currentBusiness.TenantId
                                AND targetBusiness.SharesProductPrices=1 AND targetBusiness.IsActive=1)
                            OR (currentBusiness.SharesProductPrices=0 AND price.BusinessId=@BusinessId));
                        UPDATE dbo.ProductPricePreparations SET Status=N'Superseded',SupersededAt=@Now
                        WHERE ProductId=@ProductId AND Status=N'Pending';
                        UPDATE dbo.PriceRevisionProposals SET Status=N'Superseded'
                        WHERE ProductId=@ProductId AND Status IN(N'PendingReview',N'Approved');
                        """, [P("@BusinessId", user.BusinessId), P("@ProductId", productId), P("@Now", now)], ct);
                else if (wasGenericProduct && !request.IsGenericProduct)
                    await ExecuteAsync(connection, transaction, """
                        UPDATE price SET Amount=@Amount,PreparedAmount=@Amount,
                          CostBasisType=N'Manual',CostBasisAmount=@CostBasis,
                          TargetMarginPercent=@TargetMargin,EffectiveMarginPercent=@TargetMargin,
                          InputMode=@InputMode,RoundingIncrement=@RoundingIncrement,
                          RoundingMode=@RoundingMode,PublishedAt=@Now
                        FROM dbo.ProductPrices price
                        INNER JOIN dbo.Businesses currentBusiness ON currentBusiness.BusinessId=@BusinessId
                        INNER JOIN dbo.Businesses targetBusiness ON targetBusiness.BusinessId=price.BusinessId
                        WHERE price.ProductId=@ProductId AND price.IsActive=1
                          AND ((currentBusiness.SharesProductPrices=1
                                AND targetBusiness.TenantId=currentBusiness.TenantId
                                AND targetBusiness.SharesProductPrices=1 AND targetBusiness.IsActive=1)
                            OR (currentBusiness.SharesProductPrices=0 AND price.BusinessId=@BusinessId));
                        UPDATE dbo.ProductPricePreparations SET Status=N'Superseded',SupersededAt=@Now
                        WHERE ProductId=@ProductId AND Status=N'Pending';
                        UPDATE dbo.PriceRevisionProposals SET Status=N'Superseded'
                        WHERE ProductId=@ProductId AND Status IN(N'PendingReview',N'Approved');
                        """, [P("@BusinessId", user.BusinessId), P("@ProductId", productId),
                        P("@Amount", price.Amount), P("@CostBasis", price.CostBasisAmount),
                        P("@TargetMargin", price.TargetMarginPercent), P("@InputMode", price.InputMode),
                        P("@RoundingIncrement", price.RoundingIncrement), P("@RoundingMode", price.RoundingMode),
                        P("@Now", now)], ct);
                else
                    await ExecuteAsync(connection, transaction, """
                    DECLARE @Targets TABLE(
                      BusinessId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                      PublicAmount DECIMAL(19,4) NOT NULL);
                    INSERT @Targets(BusinessId,PublicAmount)
                    SELECT price.BusinessId,price.Amount
                    FROM dbo.ProductPrices price
                    INNER JOIN dbo.Businesses currentBusiness ON currentBusiness.BusinessId=@BusinessId
                    INNER JOIN dbo.Businesses targetBusiness ON targetBusiness.BusinessId=price.BusinessId
                    WHERE price.ProductId=@ProductId AND price.IsActive=1
                      AND ((currentBusiness.SharesProductPrices=1
                            AND targetBusiness.TenantId=currentBusiness.TenantId
                            AND targetBusiness.SharesProductPrices=1 AND targetBusiness.IsActive=1)
                        OR (currentBusiness.SharesProductPrices=0 AND price.BusinessId=@BusinessId));
                    IF @@ROWCOUNT=0 THROW 51024,'The product has no active base price.',1;

                    DECLARE @CurrentPreparedAmount DECIMAL(19,4),
                            @CurrentCostBasis DECIMAL(19,6),
                            @CurrentTargetMargin DECIMAL(9,6),
                            @CurrentInputMode NVARCHAR(16),
                            @CurrentRoundingIncrement DECIMAL(19,4),
                            @CurrentRoundingMode NVARCHAR(16);
                    SELECT @CurrentPreparedAmount=COALESCE(preparation.PreparedAmount,price.Amount),
                           @CurrentCostBasis=COALESCE(preparation.CostBasisAmount,price.CostBasisAmount),
                           @CurrentTargetMargin=COALESCE(preparation.TargetMarginPercent,price.TargetMarginPercent),
                           @CurrentInputMode=COALESCE(preparation.InputMode,price.InputMode,N'Margin'),
                           @CurrentRoundingIncrement=COALESCE(preparation.RoundingIncrement,price.RoundingIncrement,1),
                           @CurrentRoundingMode=COALESCE(preparation.RoundingMode,price.RoundingMode,N'Nearest')
                    FROM dbo.ProductPrices price
                    OUTER APPLY (
                      SELECT TOP(1) pending.PreparedAmount,pending.CostBasisAmount,
                        pending.TargetMarginPercent,pending.InputMode,
                        pending.RoundingIncrement,pending.RoundingMode
                      FROM dbo.ProductPricePreparations pending
                      WHERE pending.BusinessId=@BusinessId AND pending.ProductId=@ProductId
                        AND pending.Status=N'Pending'
                      ORDER BY pending.PreparedAt DESC,pending.ProductPricePreparationId DESC
                    ) preparation
                    WHERE price.BusinessId=@BusinessId AND price.ProductId=@ProductId
                      AND price.IsActive=1;

                    IF @CurrentPreparedAmount<>@PreparedAmount
                       OR ISNULL(@CurrentCostBasis,-1)<>ISNULL(@CostBasis,-1)
                       OR ISNULL(@CurrentTargetMargin,-1)<>ISNULL(@TargetMargin,-1)
                       OR @CurrentInputMode<>@InputMode
                       OR @CurrentRoundingIncrement<>@RoundingIncrement
                       OR @CurrentRoundingMode<>@RoundingMode
                    BEGIN
                      UPDATE proposal SET Status=N'Superseded'
                      FROM dbo.PriceRevisionProposals proposal
                      INNER JOIN @Targets target ON target.BusinessId=proposal.BusinessId
                      WHERE proposal.ProductId=@ProductId
                        AND proposal.Status IN(N'PendingReview',N'Approved');

                      UPDATE preparation
                      SET Status=N'Superseded',SupersededAt=@Now
                      FROM dbo.ProductPricePreparations preparation
                      INNER JOIN @Targets target ON target.BusinessId=preparation.BusinessId
                      WHERE preparation.ProductId=@ProductId AND preparation.Status=N'Pending';

                      INSERT dbo.ProductPricePreparations
                        (ProductPricePreparationId,BusinessId,ProductId,PreparationOrigin,
                         PublicAmountSnapshot,PreparedAmount,CostBasisType,CostBasisAmount,
                         TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,
                         RoundingMode,Status,PreparedByUserId,PreparedAt)
                      SELECT NEWID(),target.BusinessId,@ProductId,N'Product',target.PublicAmount,
                             @PreparedAmount,N'Manual',@CostBasis,@TargetMargin,@TargetMargin,
                             @InputMode,@RoundingIncrement,@RoundingMode,N'Pending',@UserId,@Now
                      FROM @Targets target;
                    END;
                    """, [P("@BusinessId", user.BusinessId), P("@ProductId", productId),
                    P("@PreparedAmount", price.PreparedAmount ?? price.Amount), P("@CostBasis", price.CostBasisAmount),
                    P("@TargetMargin", price.TargetMarginPercent), P("@InputMode", price.InputMode),
                    P("@RoundingIncrement", price.RoundingIncrement), P("@RoundingMode", price.RoundingMode),
                    P("@UserId", user.UserId), P("@Now", now)], ct);
            }

            if (request.Link is { SharesPrice: true } linkedCost)
                await SqlLinkedProductCostPreparation.PrepareAsync(
                    connection, transaction, user.BusinessId, linkedCost.ParentProductId,
                    productId, user.UserId, now, ct);

            if (request.Scale is not null)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.ProductScaleConfigurations
                      (ProductId,ScaleCode,BarcodePrefix,EmbeddedValueType,ValueStart,ValueLength,DecimalPlaces,IsActive)
                    VALUES (@ProductId,@ScaleCode,@Prefix,@Type,@Start,@Length,@Decimals,1);
                    """, [P("@ProductId", productId), P("@ScaleCode", request.Scale.ScaleCode), P("@Prefix", request.Scale.BarcodePrefix),
                    P("@Type", request.Scale.EmbeddedValueType), P("@Start", request.Scale.ValueStart),
                    P("@Length", request.Scale.ValueLength), P("@Decimals", request.Scale.DecimalPlaces)], ct);
            }
            await SaveSuppliersAsync(connection, transaction, user, productId, request.Suppliers, now, ct);

            var affectedCatalogProducts = new HashSet<Guid>();
            await using (var deactivateLinks = new SqlCommand("""
                UPDATE dbo.ProductLinks SET IsActive=0,UpdatedAt=@Now
                OUTPUT deleted.ChildProductId
                WHERE TenantId=@TenantId AND ParentProductId=@ProductId AND IsActive=1;
                """, connection, transaction))
            {
                deactivateLinks.Parameters.AddRange([
                    P("@TenantId", user.TenantId), P("@ProductId", productId), P("@Now", now)
                ]);
                await using var reader = await deactivateLinks.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    affectedCatalogProducts.Add(reader.GetGuid(0));
            }

            await SqlProductLinksWriter.SaveChildrenAsync(
                connection, transaction, user.TenantId, productId,
                request.LinkedProducts ?? [], now, true, 51024, ct);
            affectedCatalogProducts.UnionWith(
                (request.LinkedProducts ?? []).Select(child => child.ChildProductId));
            if ((request.LinkedProducts ?? []).Any(child => child.SharesPrice))
                await SqlLinkedProductCostPreparation.PrepareFamilyAsync(
                    connection, transaction, user.BusinessId, productId, null, null,
                    user.UserId, now, ct);

            await ExecuteAsync(connection, transaction, """
                DECLARE @Aliases TABLE (Alias NVARCHAR(250) NOT NULL,NormalizedAlias NVARCHAR(250) NOT NULL PRIMARY KEY);
                WITH input AS (
                  SELECT JSON_VALUE([value],'$.Alias') Alias,
                         JSON_VALUE([value],'$.NormalizedAlias') NormalizedAlias,
                         ROW_NUMBER() OVER(PARTITION BY JSON_VALUE([value],'$.NormalizedAlias') ORDER BY TRY_CONVERT(INT,[key]) DESC) rowNumber
                  FROM OPENJSON(@AliasesJson))
                INSERT @Aliases(Alias,NormalizedAlias)
                SELECT Alias,NormalizedAlias FROM input WHERE rowNumber=1;
                IF EXISTS(SELECT 1 FROM dbo.ProductAliases existing
                  JOIN @Aliases requested ON requested.NormalizedAlias=existing.NormalizedAlias
                  WHERE existing.TenantId=@TenantId AND existing.Scope=0 AND existing.CustomerKey=N''
                    AND existing.ProductId<>@ProductId AND existing.Status=1 AND existing.ResolutionMode=1)
                  THROW 51024,'El alias ya resuelve a otro producto del negocio.',1;
                UPDATE existing SET Alias=requested.Alias,Kind=0,ResolutionMode=1,Source=0,
                  Status=1,UpdatedAt=@Now
                FROM dbo.ProductAliases existing
                JOIN @Aliases requested ON requested.NormalizedAlias=existing.NormalizedAlias
                WHERE existing.TenantId=@TenantId AND existing.ProductId=@ProductId
                  AND existing.Scope=0 AND existing.CustomerKey=N'';
                INSERT dbo.ProductAliases(ProductAliasId,TenantId,ProductId,Scope,CustomerKey,Alias,
                  NormalizedAlias,Kind,ResolutionMode,Source,Status,UsageCount,CreatedAt)
                SELECT NEWID(),@TenantId,@ProductId,0,N'',requested.Alias,
                  requested.NormalizedAlias,0,1,0,1,0,@Now
                FROM @Aliases requested
                WHERE NOT EXISTS(SELECT 1 FROM dbo.ProductAliases existing
                  WHERE existing.TenantId=@TenantId AND existing.ProductId=@ProductId
                    AND existing.Scope=0 AND existing.CustomerKey=N''
                    AND existing.NormalizedAlias=requested.NormalizedAlias);
                """, [P("@TenantId", user.TenantId), P("@ProductId", productId),
                    P("@AliasesJson", JsonSerializer.Serialize(request.Aliases ?? [])), P("@Now", now)], ct);

            if (request.Images is not null)
            {
                await ExecuteAsync(connection, transaction,
                    "DELETE dbo.ProductImages WHERE TenantId=@TenantId AND ProductId=@ProductId;",
                    [P("@TenantId", user.TenantId), P("@ProductId", productId)], ct);
                await ExecuteAsync(connection, transaction, """
                    DECLARE @Images TABLE (
                      ProductImageId UNIQUEIDENTIFIER NOT NULL,ProductOfferId UNIQUEIDENTIFIER NULL,
                      MediaReference NVARCHAR(1500) NOT NULL,AltText NVARCHAR(300) NULL,
                      DisplayOrder INT NOT NULL,IsPrimary BIT NOT NULL);
                    INSERT @Images
                    SELECT ProductImageId,ProductOfferId,MediaReference,AltText,DisplayOrder,IsPrimary
                    FROM OPENJSON(@ImagesJson) WITH (
                      ProductImageId UNIQUEIDENTIFIER '$.ProductImageId',
                      ProductOfferId UNIQUEIDENTIFIER '$.ProductOfferId',
                      MediaReference NVARCHAR(1500) '$.MediaReference',
                      AltText NVARCHAR(300) '$.AltText',DisplayOrder INT '$.DisplayOrder',IsPrimary BIT '$.IsPrimary');
                    IF EXISTS(SELECT 1 FROM @Images image
                      WHERE image.ProductOfferId IS NOT NULL AND NOT EXISTS(
                        SELECT 1 FROM dbo.ProductOffers offer
                        WHERE offer.ProductOfferId=image.ProductOfferId
                          AND offer.TenantId=@TenantId AND offer.ProductId=@ProductId))
                      THROW 51024,'La imagen referencia una oferta que no pertenece al producto.',1;
                    INSERT dbo.ProductImages(ProductImageId,ProductId,TenantId,ProductOfferId,MediaUrl,
                      AltText,DisplayOrder,IsPrimary,IsActive,CreatedAt)
                    SELECT ProductImageId,@ProductId,@TenantId,ProductOfferId,MediaReference,
                      AltText,DisplayOrder,IsPrimary,1,@Now FROM @Images;
                    """, [P("@TenantId", user.TenantId), P("@ProductId", productId),
                        P("@ImagesJson", JsonSerializer.Serialize(request.Images.Select(image => new {
                            image.ProductImageId, image.ProductOfferId,
                            MediaReference = image.MediaReference.Trim(), AltText = image.AltText?.Trim(),
                            image.DisplayOrder, image.IsPrimary
                        }))), P("@Now", now)], ct);
            }

            await ExecuteAsync(connection, transaction, """
                DECLARE @Change TABLE (BusinessId UNIQUEIDENTIFIER NOT NULL,CatalogChangeId BIGINT NOT NULL);
                INSERT dbo.CatalogChanges (BusinessId,ProductId,ChangeKind,OccurredAt)
                  OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @Change
                  SELECT business.BusinessId,affected.ProductId,N'Upsert',@Now
                  FROM dbo.Businesses business
                  CROSS JOIN (SELECT @ProductId ProductId
                              UNION SELECT TRY_CONVERT(UNIQUEIDENTIFIER,[value]) FROM OPENJSON(@AffectedProductIds)) affected
                  WHERE business.TenantId=@TenantId AND business.IsActive=1;
                INSERT dbo.PosSynchronizationOutboxMessages
                  (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                SELECT NEWID(),BusinessId,N'Catalog',CatalogChangeId,@Now
                FROM @Change;
                """,
                [
                    P("@TenantId", user.TenantId),
                    P("@BusinessId", user.BusinessId),
                    P("@ProductId", productId),
                    P("@AffectedProductIds", JsonSerializer.Serialize(affectedCatalogProducts)),
                    P("@Now", now)
                ], ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException exception) when (exception.Number == 51024)
        {
            await transaction.RollbackAsync(ct);
            throw new CatalogValidationException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 51021 or 51022 or 51023)
        {
            await transaction.RollbackAsync(ct);
            throw new CatalogForbiddenException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(ct);
            throw new CatalogConflictException("Product code, barcode, identifier, active price or supplier association is duplicated.");
        }

        return (await GetAsync(user.TenantId, user.BusinessId, productId, true, ct))!;
    }

    private static async Task EnsureBarcodesAvailableAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid productId,
        IEnumerable<string> values, CancellationToken ct)
    {
        var barcodes = values.Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (barcodes.Length == 0) return;
        await using var command = new SqlCommand("""
            SELECT TOP(1) barcode.Barcode,product.Name
            FROM OPENJSON(@BarcodesJson) requested
            JOIN dbo.ProductBarcodes barcode WITH (UPDLOCK,HOLDLOCK)
              ON barcode.TenantId=@TenantId AND barcode.Barcode=requested.[value]
             AND barcode.ProductId<>@ProductId
            JOIN dbo.Products product ON product.ProductId=barcode.ProductId
            ORDER BY barcode.Barcode;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@BarcodesJson", JsonSerializer.Serialize(barcodes));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            throw new CatalogConflictException(
                $"El código de barras '{reader.GetString(0)}' ya está asignado al producto '{reader.GetString(1)}' y no puede reutilizarse.");
    }

    public async Task<ProductDetail?> GetAsync(Guid tenantId, Guid businessId, Guid productId, bool includeCosts, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = ProductSelect + """
             WHERE p.TenantId=@TenantId AND b.TenantId=@TenantId AND p.ProductId=@ProductId
            """;
        command.Parameters.AddRange([P("@TenantId", tenantId), P("@BusinessId", businessId), P("@ProductId", productId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadProduct(reader, includeCosts) : null;
    }

    public async Task<ProductPage> PageAsync(Guid tenantId, Guid businessId, ProductPageRequest request, bool includeCosts, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        var comparator = request.SortDescending ? "<" : ">";
        var direction = request.SortDescending ? "DESC" : "ASC";
        command.CommandText = ProductSelect + " " + $"""
            WHERE p.TenantId=@TenantId AND b.TenantId=@TenantId
              AND (@After IS NULL OR COALESCE(p.ProductCode,p.Sku){comparator}@After)
              AND (@Code IS NULL OR COALESCE(p.ProductCode,p.Sku) LIKE @Code+'%')
              AND (@Reference IS NULL OR p.Reference LIKE @Reference+'%')
              AND (@Name IS NULL OR p.Name LIKE '%'+@Name+'%')
              AND (@Active IS NULL OR p.IsActive=@Active)
              AND (@Barcode IS NULL OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes x WHERE x.TenantId=@TenantId AND x.ProductId=p.ProductId AND x.IsActive=1 AND x.Barcode=@Barcode))
              AND (@SupplierId IS NULL OR EXISTS (SELECT 1 FROM dbo.SupplierProducts sp WHERE sp.TenantId=@TenantId AND sp.ProductId=p.ProductId AND sp.SupplierId=@SupplierId AND sp.IsActive=1))
              AND ((@MinimumPrice IS NULL AND @MaximumPrice IS NULL) OR EXISTS (SELECT 1 FROM dbo.ProductPrices fp WHERE fp.ProductId=p.ProductId AND fp.BusinessId=@BusinessId AND fp.IsActive=1
                AND (@MinimumPrice IS NULL OR fp.Amount>=@MinimumPrice)
                AND (@MaximumPrice IS NULL OR fp.Amount<=@MaximumPrice)))
            ORDER BY COALESCE(p.ProductCode,p.Sku) {direction},p.ProductId {direction} OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        command.Parameters.AddRange([P("@TenantId", tenantId), P("@BusinessId", businessId), P("@After", request.AfterProductCode),
            P("@Code", request.ProductCode), P("@Reference", request.Reference), P("@Name", request.Name),
            P("@Active", request.IsActive), P("@Barcode", request.Barcode), P("@SupplierId", request.SupplierId),
            P("@MinimumPrice", request.MinimumPrice), P("@MaximumPrice", request.MaximumPrice), P("@Take", request.PageSize)]);
        var items = new List<ProductDetail>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(ReadProduct(reader, includeCosts));
        return new ProductPage(items, items.Count == request.PageSize ? items[^1].ProductCode : null);
    }

    public async Task SetStatusAsync(CatalogUserIdentity user, Guid productId, bool isActive, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @Change TABLE (BusinessId UNIQUEIDENTIFIER NOT NULL,CatalogChangeId BIGINT NOT NULL);
            BEGIN TRANSACTION;
            UPDATE dbo.Products SET IsActive=@IsActive,UpdatedAt=@Now,UpdatedByUserId=@UserId
              WHERE ProductId=@ProductId AND TenantId=@TenantId;
            IF @@ROWCOUNT=0 BEGIN ROLLBACK; THROW 51010,'Product not found.',1; END;
            INSERT dbo.CatalogChanges (BusinessId,ProductId,ChangeKind,OccurredAt)
              OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @Change
              SELECT business.BusinessId,@ProductId,CASE WHEN @IsActive=1 THEN N'Upsert' ELSE N'Tombstone' END,@Now
              FROM dbo.Businesses business WHERE business.TenantId=@TenantId AND business.IsActive=1;
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            SELECT NEWID(),BusinessId,N'Catalog',CatalogChangeId,@Now
            FROM @Change;
            COMMIT;
            """;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId),
            P("@Now", now),
            P("@UserId", user.UserId),
            P("@IsActive", isActive),
            P("@ProductId", productId)
        ]);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<CatalogSyncSessionResponse> StartSyncAsync(
        Guid deviceId, Guid tenantId, Guid businessId, Guid warehouseId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        var sessionId = ids.NewId();
        command.CommandText = """
            IF NOT EXISTS (
              SELECT 1
              FROM dbo.EnrolledDevices d
              JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
                AND b.TenantId=d.TenantId AND b.IsActive=1
              JOIN dbo.Warehouses w ON w.WarehouseId=@WarehouseId AND w.IsActive=1 AND w.UseForSales=1
                AND w.BusinessId=b.BusinessId AND w.IsActive=1
              WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1)
              THROW 51020,'The device operational scope is invalid.',1;
            DECLARE @High BIGINT=ISNULL((SELECT MAX(CatalogChangeId) FROM dbo.CatalogChanges WHERE BusinessId=@BusinessId),0);
            INSERT dbo.CatalogSyncSessions
              (CatalogSyncSessionId,DeviceId,BusinessId,WarehouseId,HighWaterMark,CreatedAt,ExpiresAt)
            VALUES (@SessionId,@DeviceId,@BusinessId,@WarehouseId,@High,@Now,DATEADD(hour,2,@Now));
            INSERT dbo.CatalogSyncSessionProducts (CatalogSyncSessionId,ProductId)
            SELECT @SessionId,p.ProductId FROM dbo.Products p
            WHERE p.TenantId=@TenantId
              AND p.ProductCode IS NOT NULL AND p.TaxProfileId IS NOT NULL
              AND EXISTS (SELECT 1 FROM dbo.ProductPrices pr WHERE pr.ProductId=p.ProductId AND pr.BusinessId=@BusinessId AND pr.IsActive=1);
            SELECT @High,(SELECT COUNT(*) FROM dbo.CatalogSyncSessionProducts WHERE CatalogSyncSessionId=@SessionId),DATEADD(hour,2,@Now);
            """;
        command.Parameters.AddRange([P("@SessionId", sessionId), P("@DeviceId", deviceId), P("@TenantId", tenantId),
            P("@BusinessId", businessId), P("@WarehouseId", warehouseId), P("@Now", now)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new CatalogSyncSessionResponse(sessionId, reader.GetInt64(0), reader.GetInt32(1), reader.GetDateTimeOffset(2));
    }

    public async Task<CatalogBootstrapPage> BootstrapPageAsync(
        Guid deviceId, Guid sessionId, string? cursor, int pageSize, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        var high = await SessionAsync(connection, deviceId, sessionId, ct);
        var items = await PosItemsAsync(connection, """
            (@Cursor IS NULL OR p.ProductId>@Cursor)
            """, [P("@Cursor", cursor), P("@SessionId", sessionId), P("@Take", pageSize)], pageSize, sessionId, ct);
        var next = items.Count == pageSize ? items[^1].ProductId.ToString("D") : null;
        var hash = CatalogBootstrapIntegrity.Compute(items);
        return new CatalogBootstrapPage(sessionId, high, next, next is not null, hash, items);
    }

    public async Task<CatalogDeltaPage> ChangesAsync(
        Guid deviceId, Guid tenantId, Guid businessId, Guid warehouseId,
        long cursor, int pageSize, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using (var cursorCommand = connection.CreateCommand())
        {
            cursorCommand.CommandText = """
                SELECT ISNULL(MAX(c.CatalogChangeId),0) FROM dbo.CatalogChanges c
                JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId WHERE b.TenantId=@TenantId AND c.BusinessId=@BusinessId;
                """;
            cursorCommand.Parameters.AddRange([P("@TenantId", tenantId), P("@BusinessId", businessId)]);
            var maximum = Convert.ToInt64(await cursorCommand.ExecuteScalarAsync(ct));
            if (cursor > maximum) throw new CatalogValidationException("The catalog cursor is ahead of the server stream.");
        }
        await using var command = connection.CreateCommand();
        command.CommandText = """
            ;WITH LatestProductChanges AS
            (
              SELECT c.CatalogChangeId,c.BusinessId,c.ProductId,c.ChangeKind,
                     ROW_NUMBER() OVER
                     (
                       PARTITION BY c.ProductId
                       ORDER BY c.CatalogChangeId DESC
                     ) AS ProductPosition
              FROM dbo.CatalogChanges c
              WHERE c.BusinessId=@BusinessId AND c.CatalogChangeId>@Cursor
            ),
            CategoryAncestors AS
            (
              SELECT category.ProductCategoryId DescendantId,category.ProductCategoryId AncestorId,
                     category.ParentProductCategoryId
              FROM dbo.ProductCategories category WHERE category.TenantId=@TenantId
              UNION ALL
              SELECT child.DescendantId,parent.ProductCategoryId,parent.ParentProductCategoryId
              FROM CategoryAncestors child
              JOIN dbo.ProductCategories parent ON parent.ProductCategoryId=child.ParentProductCategoryId
            )
            SELECT TOP (@Take) c.CatalogChangeId,c.ChangeKind,p.ProductId,p.ProductCode,p.Reference,p.Name,p.BaseUnitCode,
              t.DianTaxCode,t.Rate,pr.Amount,pr.CurrencyCode,p.IsActive,p.IsWeighable,p.AllowsFractionalSale,
              COALESCE(pr.CostBasisAmount,0),
              CAST(CASE WHEN p.ManageStock=1 OR inventoryLink.ProductLinkId IS NOT NULL THEN 1 ELSE 0 END AS bit),
              COALESCE((SELECT Barcode AS [Value] FROM dbo.ProductBarcodes b WHERE b.ProductId=p.ProductId AND b.IsActive=1 FOR JSON PATH),N'[]'),
              COALESCE((SELECT IdentifierType AS [Type],Value FROM dbo.ProductIdentifiers i WHERE i.ProductId=p.ProductId AND i.IsActive=1 FOR JSON PATH),N'[]'),
              s.ScaleCode,s.BarcodePrefix,s.EmbeddedValueType,s.ValueStart,s.ValueLength,s.DecimalPlaces,
              p.CategoryName,p.ProductCategoryId,p.ProductBrandId,
              COALESCE((SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),ancestor.AncestorId),N',')
                        FROM CategoryAncestors ancestor
                        WHERE ancestor.DescendantId=p.ProductCategoryId),N''),
              averageCost.Amount,latestCost.Amount,
              COALESCE(pr.TargetMarginPercent,pr.EffectiveMarginPercent),
              COALESCE(inventoryLink.ParentProductId,p.ProductId),
              COALESCE(inventoryLink.InventoryFactor,1),p.IsGenericProduct
            FROM LatestProductChanges c
            JOIN dbo.Products p ON p.ProductId=c.ProductId
            JOIN dbo.TaxProfiles t ON t.TaxProfileId=p.TaxProfileId
            JOIN dbo.EnrolledDevices d ON d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
            JOIN dbo.Warehouses warehouseValue ON warehouseValue.WarehouseId=@WarehouseId
              AND warehouseValue.BusinessId=b.BusinessId AND warehouseValue.IsActive=1
            JOIN dbo.ProductPrices pr ON pr.ProductId=p.ProductId AND pr.BusinessId=c.BusinessId AND pr.IsActive=1
            LEFT JOIN dbo.ProductScaleConfigurations s ON s.ProductId=p.ProductId AND s.IsActive=1
            LEFT JOIN dbo.ProductLinks inventoryLink
              ON inventoryLink.TenantId=@TenantId
             AND inventoryLink.ChildProductId=p.ProductId
             AND inventoryLink.SharesInventory=1 AND inventoryLink.IsActive=1
            OUTER APPLY
            (
              SELECT COALESCE(MAX(NULLIF(balance.AverageUnitCost,0)),pr.CostBasisAmount,0) Amount
              FROM dbo.InventoryBalances balance
              WHERE balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId
                AND balance.ProductId=p.ProductId
            ) averageCost
            OUTER APPLY
            (
              SELECT COALESCE(
                (SELECT TOP (1) latest.LatestUnitCost
                 FROM dbo.SupplierProductLatestCosts latest
                 WHERE latest.BusinessId=@BusinessId AND latest.ProductId=p.ProductId
                 ORDER BY latest.ObservedAt DESC,latest.SupplierId),
                pr.CostBasisAmount,averageCost.Amount,0) Amount
            ) latestCost
            WHERE c.ProductPosition=1
            ORDER BY c.CatalogChangeId;
            """;
        command.Parameters.AddRange([P("@Take", pageSize + 1), P("@DeviceId", deviceId), P("@TenantId", tenantId),
            P("@BusinessId", businessId), P("@WarehouseId", warehouseId), P("@Cursor", cursor)]);
        var changes = new List<CatalogDelta>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var version = reader.GetInt64(0);
            var kind = reader.GetString(1);
            changes.Add(new CatalogDelta(version, kind, ReadPosItem(reader, 2)));
        }
        var hasMore = changes.Count > pageSize;
        if (hasMore) changes.RemoveAt(changes.Count - 1);
        return new CatalogDeltaPage(cursor, changes.Count == 0 ? cursor : changes[^1].Version, hasMore, changes);
    }

    public async Task<InventoryAvailabilityResponse> AvailabilityAsync(
        Guid deviceId, Guid tenantId, Guid businessId,
        InventoryAvailabilityRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.AllowNegativeStockSales,
              COALESCE(balance.QuantityOnHand,0) / COALESCE(NULLIF(link.InventoryFactor,0),1)
            FROM dbo.EnrolledDevices d
            JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
              AND b.TenantId=d.TenantId AND b.IsActive=1
            JOIN dbo.Warehouses w ON w.WarehouseId=@WarehouseId AND w.IsActive=1 AND w.UseForSales=1
              AND w.BusinessId=b.BusinessId AND w.IsActive=1
            JOIN dbo.Products p ON p.ProductId=@ProductId
              AND p.TenantId=@TenantId
            LEFT JOIN dbo.ProductLinks link
              ON link.TenantId=p.TenantId AND link.ChildProductId=p.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.InventoryBalances balance WITH (UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=@BusinessId AND balance.WarehouseId=w.WarehouseId
             AND balance.ProductId=COALESCE(link.ParentProductId,p.ProductId)
            WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1;
            """;
        command.Parameters.AddRange([P("@DeviceId", deviceId), P("@TenantId", tenantId), P("@BusinessId", businessId),
            P("@WarehouseId", request.WarehouseId), P("@ProductId", request.ProductId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new CatalogForbiddenException("The warehouse is not assigned to this device.");
        var allowsNegative = reader.GetBoolean(0);
        var available = reader.GetDecimal(1);
        return new InventoryAvailabilityResponse(request.ProductId, request.WarehouseId, request.Quantity, available,
            !allowsNegative, allowsNegative || available >= request.Quantity,
            allowsNegative ? "NotRequired" : available >= request.Quantity ? "Available" : "Insufficient");
    }

    public async Task<IReadOnlyList<InventoryAvailabilityResponse>> AvailabilityBatchAsync(
        Guid deviceId, Guid tenantId, Guid businessId,
        InventoryAvailabilityBatchRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH requested AS (
              SELECT CONVERT(INT,[key]) Ordinal,
                     TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE([value],'$.ProductId')) ProductId,
                     TRY_CONVERT(DECIMAL(19,6),JSON_VALUE([value],'$.Quantity')) Quantity
              FROM OPENJSON(@Items)
            )
            SELECT requested.ProductId,requested.Quantity,w.AllowNegativeStockSales,
                   COALESCE(balance.QuantityOnHand,0) / COALESCE(NULLIF(link.InventoryFactor,0),1)
            FROM dbo.EnrolledDevices d
            JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
              AND b.TenantId=d.TenantId AND b.IsActive=1
            JOIN dbo.Warehouses w ON w.WarehouseId=@WarehouseId AND w.IsActive=1 AND w.UseForSales=1
              AND w.BusinessId=b.BusinessId
            CROSS JOIN requested
            JOIN dbo.Products p ON p.ProductId=requested.ProductId AND p.TenantId=@TenantId
            LEFT JOIN dbo.ProductLinks link
              ON link.TenantId=p.TenantId AND link.ChildProductId=p.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.InventoryBalances balance WITH (UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=@BusinessId AND balance.WarehouseId=w.WarehouseId
             AND balance.ProductId=COALESCE(link.ParentProductId,p.ProductId)
            WHERE d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1
            ORDER BY requested.Ordinal;
            """;
        command.Parameters.AddRange([
            P("@DeviceId", deviceId), P("@TenantId", tenantId), P("@BusinessId", businessId),
            P("@WarehouseId", request.WarehouseId), P("@Items", JsonSerializer.Serialize(request.Items))
        ]);
        var results = new List<InventoryAvailabilityResponse>(request.Items.Count);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var productId = reader.GetGuid(0);
            var quantity = reader.GetDecimal(1);
            var allowsNegative = reader.GetBoolean(2);
            var available = reader.GetDecimal(3);
            results.Add(new InventoryAvailabilityResponse(
                productId, request.WarehouseId, quantity, available,
                !allowsNegative, allowsNegative || available >= quantity,
                allowsNegative ? "NotRequired" : available >= quantity ? "Available" : "Insufficient"));
        }
        if (results.Count != request.Items.Count)
            throw new CatalogForbiddenException("The warehouse or one of the products is not available to this device.");
        return results;
    }

    public async Task<IReadOnlyList<ProductWarehouseAvailabilityItem>> WarehouseAvailabilityAsync(
        Guid? deviceId,
        Guid tenantId,
        Guid businessId,
        Guid productId,
        bool includeOtherBusinesses,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF @DeviceId IS NOT NULL AND NOT EXISTS(
                SELECT 1
                FROM dbo.EnrolledDevices device
                JOIN dbo.PosEnrollmentSessions enrollment
                  ON enrollment.DeviceId=device.DeviceId
                 AND enrollment.BusinessId=@BusinessId
                 AND enrollment.RedeemedAt IS NOT NULL
                WHERE device.DeviceId=@DeviceId
                  AND device.TenantId=@TenantId
                  AND device.IsActive=1)
              THROW 51011,'The device is not enrolled for the requested business.',1;

            IF NOT EXISTS(
                SELECT 1
                FROM dbo.Products product
                WHERE product.TenantId=@TenantId AND product.ProductId=@ProductId
                  AND product.IsActive=1)
              THROW 51012,'The product is not available in the current business.',1;

            ;WITH originProduct AS(
                SELECT product.ProductId,
                       COALESCE(product.ProductCode,product.Sku,product.Reference,N'') ProductCode
                FROM dbo.Products product
                WHERE product.ProductId=@ProductId
                  AND product.IsActive=1
                  AND product.ManageStock=1
                  AND product.TenantId=@TenantId
            ),
            scopedProducts AS(
                SELECT business.BusinessId,product.ProductId,product.ProductCode
                FROM originProduct product
                CROSS JOIN dbo.Businesses business
                WHERE business.TenantId=@TenantId
                  AND business.IsActive=1
                  AND (business.BusinessId=@BusinessId OR @IncludeOtherBusinesses=1)
            )
            SELECT business.BusinessId,business.Name,
                   warehouse.WarehouseId,warehouse.Code,warehouse.Name,
                   product.ProductId,product.ProductCode,
                   COALESCE(balance.QuantityOnHand,0),
                   CONVERT(bit,CASE WHEN business.BusinessId=@BusinessId THEN 1 ELSE 0 END)
            FROM scopedProducts product
            JOIN dbo.Businesses business ON business.BusinessId=product.BusinessId
            JOIN dbo.Warehouses warehouse
              ON warehouse.BusinessId=business.BusinessId
             AND warehouse.IsActive=1
             AND warehouse.IsSystem=0
            LEFT JOIN dbo.InventoryBalances balance
              ON balance.BusinessId=product.BusinessId
             AND balance.WarehouseId=warehouse.WarehouseId
             AND balance.ProductId=product.ProductId
            ORDER BY CASE WHEN business.BusinessId=@BusinessId THEN 0 ELSE 1 END,
                     business.Name,warehouse.Name,warehouse.WarehouseId;
            """;
        command.Parameters.AddRange([
            P("@DeviceId", deviceId),
            P("@TenantId", tenantId),
            P("@BusinessId", businessId),
            P("@ProductId", productId),
            P("@IncludeOtherBusinesses", includeOtherBusinesses)
        ]);
        var result = new List<ProductWarehouseAvailabilityItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new ProductWarehouseAvailabilityItem(
                reader.GetGuid(0), reader.GetString(1),
                reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
                reader.GetGuid(5), reader.GetString(6), reader.GetDecimal(7), reader.GetBoolean(8)));
        return result;
    }

    private const string ProductSelect = """
        SELECT p.ProductId,@BusinessId,COALESCE(p.ProductCode,p.Sku),p.Reference,p.Name,p.IsActive,
          (SELECT Barcode AS [Value] FROM dbo.ProductBarcodes barcode WHERE barcode.ProductId=p.ProductId AND barcode.TenantId=@TenantId AND barcode.IsActive=1 FOR JSON PATH),
          (SELECT price.Amount,price.CurrencyCode,
                  COALESCE(preparation.CostBasisAmount,price.CostBasisAmount) CostBasisAmount,
                  COALESCE(preparation.TargetMarginPercent,price.TargetMarginPercent) TargetMarginPercent,
                  COALESCE(preparation.PreparedAmount,price.Amount) PreparedAmount,
                  COALESCE(preparation.InputMode,price.InputMode,N'Margin') InputMode,
                  COALESCE(preparation.RoundingIncrement,price.RoundingIncrement,1) RoundingIncrement,
                  COALESCE(preparation.RoundingMode,price.RoundingMode,N'Nearest') RoundingMode
             FROM dbo.ProductPrices price
             OUTER APPLY (
               SELECT TOP(1) pending.PreparedAmount,pending.CostBasisAmount,
                 pending.TargetMarginPercent,pending.InputMode,
                 pending.RoundingIncrement,pending.RoundingMode
               FROM dbo.ProductPricePreparations pending
               WHERE pending.BusinessId=price.BusinessId AND pending.ProductId=price.ProductId
                 AND pending.Status=N'Pending'
               ORDER BY pending.PreparedAt DESC,pending.ProductPricePreparationId DESC
             ) preparation
             WHERE price.ProductId=p.ProductId AND price.BusinessId=@BusinessId
               AND price.IsActive=1 FOR JSON PATH),
          (SELECT s.SupplierId,s.Identification,s.Name,sp.SupplierProductCode,c.BaseUnitCost,sp.IsPrimary,sp.PurchasePresentationName,sp.UnitsPerPresentation
             FROM dbo.SupplierProducts sp JOIN dbo.Suppliers s ON s.SupplierId=sp.SupplierId
             JOIN dbo.SupplierCostAgreements c ON c.SupplierProductId=sp.SupplierProductId AND c.IsActive=1
             WHERE sp.ProductId=p.ProductId AND sp.TenantId=@TenantId AND sp.IsActive=1 FOR JSON PATH),
          p.TaxProfileId,p.PurchaseTaxProfileId,p.PurchaseTaxTreatment,p.Description,p.BaseUnitCode,p.ManageStock,p.IsWeighable,p.UnitGrossWeightKg,p.IsGenericProduct
        FROM dbo.Products p
        JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
        """;

    private static ProductDetail ReadProduct(SqlDataReader reader, bool includeCosts)
    {
        var barcodes = reader.IsDBNull(6)
            ? []
            : (JsonSerializer.Deserialize<BarcodeJson[]>(reader.GetString(6)) ?? [])
                .Select(value => value.Value)
                .ToArray();
        var prices = reader.IsDBNull(7)
            ? []
            : JsonSerializer.Deserialize<ProductPriceInput[]>(reader.GetString(7)) ?? [];
        var supplierCosts = !includeCosts
            ? null
            : reader.IsDBNull(8)
                ? []
                : JsonSerializer.Deserialize<SupplierCostInput[]>(reader.GetString(8)) ?? [];

        return new ProductDetail(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            barcodes,
            prices,
            supplierCosts,
            reader.IsDBNull(9) ? Guid.Empty : reader.GetGuid(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? "EA" : reader.GetString(13),
            reader.GetBoolean(14),
            reader.GetBoolean(15),
            reader.IsDBNull(16) ? null : reader.GetDecimal(16),
            reader.GetBoolean(17));
    }

    private async Task<List<PosCatalogItem>> PosItemsAsync(
        SqlConnection connection, string predicate, SqlParameter[] parameters, int take,
        Guid sessionId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            ;WITH CategoryAncestors AS
            (
              SELECT category.ProductCategoryId DescendantId,category.ProductCategoryId AncestorId,
                     category.ParentProductCategoryId
              FROM dbo.ProductCategories category
              WHERE category.TenantId=(SELECT b.TenantId FROM dbo.CatalogSyncSessions ss JOIN dbo.Businesses b ON b.BusinessId=ss.BusinessId WHERE ss.CatalogSyncSessionId=@SessionId)
              UNION ALL
              SELECT child.DescendantId,parent.ProductCategoryId,parent.ParentProductCategoryId
              FROM CategoryAncestors child
              JOIN dbo.ProductCategories parent ON parent.ProductCategoryId=child.ParentProductCategoryId
            )
            SELECT TOP (@Take) p.ProductId,p.ProductCode,p.Reference,p.Name,p.BaseUnitCode,t.DianTaxCode,t.Rate,
              pr.Amount,pr.CurrencyCode,p.IsActive,p.IsWeighable,p.AllowsFractionalSale,
              COALESCE(pr.CostBasisAmount,0),
              CAST(CASE WHEN p.ManageStock=1 OR inventoryLink.ProductLinkId IS NOT NULL THEN 1 ELSE 0 END AS bit),
              (SELECT Barcode AS [Value] FROM dbo.ProductBarcodes b WHERE b.ProductId=p.ProductId AND b.IsActive=1 FOR JSON PATH),
              (SELECT IdentifierType AS [Type],Value FROM dbo.ProductIdentifiers i WHERE i.ProductId=p.ProductId AND i.IsActive=1 FOR JSON PATH),
              s.ScaleCode,s.BarcodePrefix,s.EmbeddedValueType,s.ValueStart,s.ValueLength,s.DecimalPlaces,
              p.CategoryName,p.ProductCategoryId,p.ProductBrandId,
              COALESCE((SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),ancestor.AncestorId),N',')
                        FROM CategoryAncestors ancestor
                        WHERE ancestor.DescendantId=p.ProductCategoryId),N''),
              averageCost.Amount,latestCost.Amount,
              COALESCE(pr.TargetMarginPercent,pr.EffectiveMarginPercent),
              COALESCE(inventoryLink.ParentProductId,p.ProductId),
              COALESCE(inventoryLink.InventoryFactor,1),p.IsGenericProduct
            FROM dbo.CatalogSyncSessions ss
            JOIN dbo.CatalogSyncSessionProducts ssp ON ssp.CatalogSyncSessionId=ss.CatalogSyncSessionId
            JOIN dbo.Products p ON p.ProductId=ssp.ProductId
            JOIN dbo.TaxProfiles t ON t.TaxProfileId=p.TaxProfileId
            JOIN dbo.ProductPrices pr ON pr.ProductId=p.ProductId AND pr.BusinessId=ss.BusinessId AND pr.IsActive=1
            LEFT JOIN dbo.ProductScaleConfigurations s ON s.ProductId=p.ProductId AND s.IsActive=1
            LEFT JOIN dbo.ProductLinks inventoryLink
              ON inventoryLink.TenantId=p.TenantId
             AND inventoryLink.ChildProductId=p.ProductId
             AND inventoryLink.SharesInventory=1 AND inventoryLink.IsActive=1
            OUTER APPLY
            (
              SELECT COALESCE(MAX(NULLIF(balance.AverageUnitCost,0)),pr.CostBasisAmount,0) Amount
              FROM dbo.InventoryBalances balance
              WHERE balance.BusinessId=ss.BusinessId AND balance.WarehouseId=ss.WarehouseId
                AND balance.ProductId=p.ProductId
            ) averageCost
            OUTER APPLY
            (
              SELECT COALESCE(
                (SELECT TOP (1) latest.LatestUnitCost
                 FROM dbo.SupplierProductLatestCosts latest
                 WHERE latest.BusinessId=ss.BusinessId AND latest.ProductId=p.ProductId
                 ORDER BY latest.ObservedAt DESC,latest.SupplierId),
                pr.CostBasisAmount,averageCost.Amount,0) Amount
            ) latestCost
            WHERE ss.CatalogSyncSessionId=@SessionId AND {predicate}
            ORDER BY p.ProductId;
            """;
        command.Parameters.AddRange(parameters);
        var items = new List<PosCatalogItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(ReadPosItem(reader, 0));
        return items;
    }

    private static PosCatalogItem ReadPosItem(SqlDataReader reader, int offset)
    {
        ScaleConfigurationInput? scale = reader.IsDBNull(offset + 16) ? null :
            new(reader.GetString(offset + 16), reader.GetString(offset + 17), reader.GetString(offset + 18),
                reader.GetInt32(offset + 19), reader.GetInt32(offset + 20), reader.GetInt32(offset + 21));
        return new(reader.GetGuid(offset), reader.GetString(offset + 1), reader.IsDBNull(offset + 2) ? null : reader.GetString(offset + 2),
            reader.GetString(offset + 3), reader.GetString(offset + 4), reader.GetString(offset + 5), reader.GetDecimal(offset + 6),
            reader.GetDecimal(offset + 7), reader.GetString(offset + 8), reader.GetBoolean(offset + 9),
            reader.GetBoolean(offset + 10), reader.GetBoolean(offset + 11), scale,
            DeserializeArray<BarcodeJson>(reader, offset + 14).Select(value => value.Value).ToArray(),
            DeserializeArray<ProductIdentifierInput>(reader, offset + 15),
            reader.GetDecimal(offset + 12), reader.GetBoolean(offset + 13),
            reader.IsDBNull(offset + 22) ? null : reader.GetString(offset + 22),
            reader.IsDBNull(offset + 23) ? null : reader.GetGuid(offset + 23),
            reader.IsDBNull(offset + 24) ? null : reader.GetGuid(offset + 24),
            reader.GetString(offset + 25).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(Guid.Parse).ToArray(),
            reader.GetDecimal(offset + 26),reader.GetDecimal(offset + 27),
            reader.IsDBNull(offset + 28) ? null : reader.GetDecimal(offset + 28),
            reader.GetGuid(offset + 29),reader.GetDecimal(offset + 30),reader.GetBoolean(offset + 31));
    }

    private static T[] DeserializeArray<T>(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? []
            : JsonSerializer.Deserialize<T[]>(reader.GetString(ordinal)) ?? [];

    private static async Task<long> SessionAsync(SqlConnection connection, Guid deviceId, Guid sessionId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT HighWaterMark FROM dbo.CatalogSyncSessions WHERE CatalogSyncSessionId=@SessionId AND DeviceId=@DeviceId AND ExpiresAt>SYSUTCDATETIME();";
        command.Parameters.AddRange([P("@SessionId", sessionId), P("@DeviceId", deviceId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new CatalogForbiddenException("The catalog sync session is invalid or expired.");
        return reader.GetInt64(0);
    }

    private static async Task EnsurePriceUnchangedAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid productId, decimal requestedPrice, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Amount FROM dbo.ProductPrices WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@ProductId", productId)]);
        var current = await command.ExecuteScalarAsync(ct);
        if (current is null || current is DBNull)
            throw new CatalogValidationException("The product has no active base price.");
        if (Convert.ToDecimal(current) != requestedPrice)
            throw new CatalogValidationException(
                "Sale prices must be changed from Products > Prices and profitability.");
    }

    private static async Task<bool> ReadIsGenericProductAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId,
        Guid productId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT IsGenericProduct FROM dbo.Products WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND ProductId=@ProductId;";
        command.Parameters.AddRange([P("@TenantId", tenantId), P("@ProductId", productId)]);
        var value = await command.ExecuteScalarAsync(ct);
        if (value is null || value is DBNull)
            throw new CatalogValidationException("The product was not found in the authenticated scope.");
        return Convert.ToBoolean(value);
    }

    private static async Task<string> NextProductCodeAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, CancellationToken ct)
    {
        const string sql = """
            DECLARE @Next INT;
            SELECT @Next=COALESCE(MAX(TRY_CONVERT(INT,SUBSTRING(ProductCode,5,10))),0)+1
            FROM dbo.Products WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId
              AND ProductCode LIKE N'PRD-%';
            SELECT @Next;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        var next = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        return $"PRD-{next:D6}";
    }

    private async Task SaveSuppliersAsync(
        SqlConnection connection, SqlTransaction transaction, CatalogUserIdentity user,
        Guid productId, IReadOnlyCollection<SupplierCostInput> suppliers,
        DateTimeOffset now, CancellationToken ct)
    {
        var rows = suppliers.Select(supplier => new
        {
            supplier.SupplierId,
            PartyId = ids.NewId(),
            SupplierProductId = ids.NewId(),
            CostId = ids.NewId(),
            supplier.Identification,
            supplier.Name,
            Code = supplier.SupplierProductCode,
            supplier.IsPrimary,
            PresentationName = supplier.PurchasePresentationName.Trim(),
            supplier.UnitsPerPresentation,
            Cost = supplier.BaseUnitCost
        });
        await ExecuteAsync(connection, transaction, """
            DECLARE @Input TABLE (
              SupplierId UNIQUEIDENTIFIER NOT NULL,
              PartyId UNIQUEIDENTIFIER NOT NULL,
              SupplierProductId UNIQUEIDENTIFIER NOT NULL,
              CostId UNIQUEIDENTIFIER NOT NULL,
              Identification NVARCHAR(40) NOT NULL,
              Name NVARCHAR(200) NOT NULL,
              Code NVARCHAR(120) NULL,
              IsPrimary BIT NOT NULL,
              PresentationName NVARCHAR(80) NOT NULL,
              UnitsPerPresentation DECIMAL(19,6) NOT NULL,
              Cost DECIMAL(19,4) NOT NULL);
            INSERT @Input
            SELECT SupplierId,PartyId,SupplierProductId,CostId,Identification,Name,
                   Code,IsPrimary,PresentationName,UnitsPerPresentation,Cost
            FROM OPENJSON(@SuppliersJson) WITH (
              SupplierId UNIQUEIDENTIFIER '$.SupplierId',
              PartyId UNIQUEIDENTIFIER '$.PartyId',
              SupplierProductId UNIQUEIDENTIFIER '$.SupplierProductId',
              CostId UNIQUEIDENTIFIER '$.CostId',
              Identification NVARCHAR(40) '$.Identification',
              Name NVARCHAR(200) '$.Name',
              Code NVARCHAR(120) '$.Code',
              IsPrimary BIT '$.IsPrimary',
              PresentationName NVARCHAR(80) '$.PresentationName',
              UnitsPerPresentation DECIMAL(19,6) '$.UnitsPerPresentation',
              Cost DECIMAL(19,4) '$.Cost');

            UPDATE input SET SupplierId=existing.SupplierId
            FROM @Input input
            JOIN dbo.Suppliers existing WITH(UPDLOCK,HOLDLOCK)
              ON existing.TenantId=@TenantId AND existing.Identification=input.Identification;
            IF EXISTS (SELECT 1 FROM @Input input
                       JOIN dbo.Suppliers existing ON existing.SupplierId=input.SupplierId
                       WHERE existing.TenantId<>@TenantId)
              THROW 51023,'The supplier is outside the authenticated scope.',1;

            INSERT dbo.Parties
              (PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            SELECT input.PartyId,@TenantId,N'Organization',input.Name,input.Name,
                   N'Incomplete',1,@UserId,@Now
            FROM @Input input
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Suppliers existing WHERE existing.SupplierId=input.SupplierId);
            INSERT dbo.Suppliers(SupplierId,TenantId,PartyId,Identification,Name,IsActive,CreatedAt)
            SELECT input.SupplierId,@TenantId,input.PartyId,input.Identification,input.Name,1,@Now
            FROM @Input input
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Suppliers existing WHERE existing.SupplierId=input.SupplierId);

            IF EXISTS (SELECT 1 FROM @Input WHERE IsPrimary=1)
              UPDATE dbo.SupplierProducts SET IsPrimary=0
              WHERE TenantId=@TenantId AND ProductId=@ProductId AND IsActive=1;
            UPDATE existing
            SET SupplierProductCode=input.Code,
                PurchasePresentationName=input.PresentationName,
                UnitsPerPresentation=input.UnitsPerPresentation,
                IsPrimary=input.IsPrimary,IsActive=1
            FROM dbo.SupplierProducts existing
            JOIN @Input input ON input.SupplierId=existing.SupplierId
            WHERE existing.TenantId=@TenantId AND existing.ProductId=@ProductId;
            INSERT dbo.SupplierProducts
              (SupplierProductId,TenantId,ProductId,SupplierId,SupplierProductCode,
               PurchasePresentationName,UnitsPerPresentation,IsPrimary,IsActive,CreatedAt)
            SELECT input.SupplierProductId,@TenantId,@ProductId,input.SupplierId,input.Code,
                   input.PresentationName,input.UnitsPerPresentation,input.IsPrimary,1,@Now
            FROM @Input input
            WHERE NOT EXISTS (
              SELECT 1 FROM dbo.SupplierProducts existing WITH(UPDLOCK,HOLDLOCK)
              WHERE existing.TenantId=@TenantId AND existing.ProductId=@ProductId
                AND existing.SupplierId=input.SupplierId);
            UPDATE input SET SupplierProductId=existing.SupplierProductId
            FROM @Input input
            JOIN dbo.SupplierProducts existing
              ON existing.TenantId=@TenantId AND existing.ProductId=@ProductId
             AND existing.SupplierId=input.SupplierId;

            DECLARE @CostChanges TABLE (
              SupplierProductId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
              CostId UNIQUEIDENTIFIER NOT NULL,
              Cost DECIMAL(19,4) NOT NULL);
            INSERT @CostChanges
            SELECT input.SupplierProductId,input.CostId,input.Cost
            FROM @Input input
            WHERE NOT EXISTS (
              SELECT 1 FROM dbo.SupplierCostAgreements agreement WITH(UPDLOCK,HOLDLOCK)
              WHERE agreement.SupplierProductId=input.SupplierProductId
                AND agreement.IsActive=1 AND agreement.BaseUnitCost=input.Cost);
            UPDATE agreement SET IsActive=0,ValidUntil=@Now
            FROM dbo.SupplierCostAgreements agreement
            JOIN @CostChanges changed ON changed.SupplierProductId=agreement.SupplierProductId
            WHERE agreement.IsActive=1;
            INSERT dbo.SupplierCostAgreements
              (SupplierCostAgreementId,SupplierProductId,BaseUnitCost,CurrencyCode,ValidFrom,IsActive,CreatedAt)
            SELECT CostId,SupplierProductId,Cost,N'COP',@Now,1,@Now FROM @CostChanges;
            """, [
            P("@TenantId", user.TenantId), P("@UserId", user.UserId),
            P("@ProductId", productId), P("@Now", now),
            P("@SuppliersJson", JsonSerializer.Serialize(rows))
        ], ct);
    }

    private static SqlParameter[] ProductParameters(CatalogUserIdentity user, Guid id, SaveProductRequest r, DateTimeOffset now) =>
        [P("@ProductId", id), P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@ProductCode", r.ProductCode.Trim()),
         P("@Reference", r.Reference), P("@Name", r.Name.Trim()), P("@Description", r.Description), P("@BaseUnitCode", r.BaseUnitCode.Trim()),
         P("@TaxProfileId", r.TaxProfileId), P("@PurchaseTaxProfileId", r.PurchaseTaxProfileId),
         P("@PurchaseTaxTreatment", r.PurchaseTaxTreatment), P("@ManageInventory", r.ManageInventory), P("@IsGenericProduct", r.IsGenericProduct), P("@IsWeighable", r.IsWeighable),
         P("@UnitGrossWeightKg", r.UnitGrossWeightKg),
         P("@ConversionMaximumLossPercent", r.ConversionMaximumLossPercent),
         P("@ProductCategoryId", r.ProductCategoryId), P("@ProductBrandId", r.ProductBrandId), P("@AllowsFractionalSale", r.AllowsFractionalSale),
         P("@ParentProductId", r.Link?.ParentProductId),
         P("@SharesInventory", r.Link?.SharesInventory ?? false),
         P("@AllowsConversion", r.Link?.AllowsConversion ?? false),
         P("@Now", now), P("@UserId", user.UserId)];

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, SqlParameter[] parameters, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
private sealed record BarcodeJson(string Value);
}
