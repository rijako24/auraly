using System.Data;
using System.Security.Cryptography;
using System.Text;
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
            await EnsureBarcodesAvailableAsync(connection, transaction, user.BusinessId, productId, request.Barcodes.Select(value => value.Value), ct);
            if (create)
                request = request with { ProductCode = await NextProductCodeAsync(connection, transaction, user.BusinessId, ct) };
            if (!create)
                await EnsurePriceUnchangedAsync(connection, transaction, user.BusinessId, productId, request.Prices.Single().Amount, ct);
            await ExecuteAsync(connection, transaction, """
                IF NOT EXISTS (
                  SELECT 1 FROM dbo.TaxProfiles t JOIN dbo.Businesses b ON b.BusinessId=t.BusinessId
                  WHERE t.TaxProfileId=@TaxProfileId AND t.BusinessId=@BusinessId AND b.TenantId=@TenantId AND t.IsActive=1)
                  THROW 51021, 'The sales VAT profile is outside the authenticated scope or inactive.', 1;
                IF NOT EXISTS (
                  SELECT 1 FROM dbo.TaxProfiles t
                  WHERE t.TaxProfileId=@PurchaseTaxProfileId AND t.BusinessId=@BusinessId AND t.IsActive=1)
                  THROW 51021, 'The purchase VAT profile is outside the authenticated scope or inactive.', 1;
                IF EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@PurchaseTaxProfileId AND BusinessId=@BusinessId AND Rate=0) AND @PurchaseTaxTreatment<>N'NotApplicable'
                  THROW 51024, 'A zero-rated purchase VAT profile must use NotApplicable treatment.', 1;
                IF EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@PurchaseTaxProfileId AND BusinessId=@BusinessId AND Rate>0) AND @PurchaseTaxTreatment=N'NotApplicable'
                  THROW 51024, 'A positive purchase VAT profile must use DeductibleInputVat or CapitalizedCost treatment.', 1;
                IF NOT EXISTS (SELECT 1 FROM dbo.ProductUnits WHERE BusinessId=@BusinessId AND Code=@BaseUnitCode AND IsActive=1)
                  THROW 51021, 'The product unit is outside the authenticated scope or inactive.', 1;
                IF @ProductCategoryId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ProductCategories WHERE BusinessId=@BusinessId AND ProductCategoryId=@ProductCategoryId AND IsActive=1)
                  THROW 51021, 'The product category is outside the authenticated scope or inactive.', 1;
                IF @ProductBrandId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ProductBrands WHERE BusinessId=@BusinessId AND ProductBrandId=@ProductBrandId AND IsActive=1)
                  THROW 51021, 'The product brand is outside the authenticated scope or inactive.', 1;
                IF @ParentProductId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Products WHERE BusinessId=@BusinessId AND ProductId=@ParentProductId AND IsActive=1)
                  THROW 51021, 'The linked parent product is outside the authenticated scope or inactive.', 1;
                IF @ParentProductId=@ProductId
                  THROW 51022, 'A product cannot be linked to itself.', 1;
                IF @ParentProductId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.ProductLinks WHERE BusinessId=@BusinessId AND ChildProductId=@ParentProductId AND IsActive=1)
                  THROW 51022, 'Linked product chains are not allowed.', 1;
                IF @ParentProductId IS NOT NULL AND EXISTS (
                  SELECT 1 FROM dbo.InventoryBalances
                  WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND QuantityOnHand<>0)
                  THROW 51024, 'El producto tiene existencias. Deja su inventario en cero antes de vincularlo.', 1;
                IF @ManageInventory=0 AND EXISTS (
                  SELECT 1 FROM dbo.ProductLinks WHERE BusinessId=@BusinessId AND ParentProductId=@ProductId
                    AND IsActive=1 AND AllowsConversion=1)
                  THROW 51024, 'A conversion family root must manage inventory.', 1;
                IF @AllowsConversion=1 AND NOT EXISTS (
                  SELECT 1 FROM dbo.Products WHERE BusinessId=@BusinessId AND ProductId=@ParentProductId
                    AND IsActive=1 AND ManageStock=1 AND ConversionMaximumLossPercent IS NOT NULL)
                  THROW 51024, 'The linked parent must manage inventory and define a maximum conversion loss.', 1;
                """, ProductParameters(user, productId, request, now), ct);
            await ExecuteAsync(connection, transaction, create
                ? """
                  INSERT dbo.Products
                    (ProductId,BusinessId,ProductCode,Reference,Sku,Name,Description,ProductCategoryId,CategoryName,ProductBrandId,BaseUnitCode,TaxProfileId,
                     PurchaseTaxProfileId,PurchaseTaxTreatment,ManageStock,ConversionMaximumLossPercent,AllowsFractionalSale,IsWeighable,IsActive,Source,UnitPrice,Currency,CreatedAt,UpdatedAt,CreatedByUserId,UpdatedByUserId)
                  VALUES
                    (@ProductId,@BusinessId,@ProductCode,@Reference,@Reference,@Name,@Description,@ProductCategoryId,(SELECT Name FROM dbo.ProductCategories WHERE ProductCategoryId=@ProductCategoryId),@ProductBrandId,@BaseUnitCode,@TaxProfileId,
                     @PurchaseTaxProfileId,@PurchaseTaxTreatment,@ManageInventory,@ConversionMaximumLossPercent,@AllowsFractionalSale,@IsWeighable,1,0,@InitialPrice,N'COP',@Now,NULL,@UserId,NULL);
                  """
                : """
                  UPDATE dbo.Products SET ProductCode=@ProductCode,Reference=@Reference,Sku=@Reference,Name=@Name,
                    Description=@Description,ProductCategoryId=@ProductCategoryId,CategoryName=(SELECT Name FROM dbo.ProductCategories WHERE ProductCategoryId=@ProductCategoryId),ProductBrandId=@ProductBrandId,BaseUnitCode=@BaseUnitCode,TaxProfileId=@TaxProfileId,
                    PurchaseTaxProfileId=@PurchaseTaxProfileId,PurchaseTaxTreatment=@PurchaseTaxTreatment,ManageStock=@ManageInventory,ConversionMaximumLossPercent=@ConversionMaximumLossPercent,AllowsFractionalSale=@AllowsFractionalSale,IsWeighable=@IsWeighable,UpdatedAt=@Now,UpdatedByUserId=@UserId
                  WHERE ProductId=@ProductId AND BusinessId=@BusinessId;
                  IF @@ROWCOUNT=0 THROW 51010, 'Product was not found in the authenticated scope.', 1;
                  DELETE FROM dbo.ProductBarcodes WHERE ProductId=@ProductId;
                  DELETE FROM dbo.ProductIdentifiers WHERE ProductId=@ProductId;
                  DELETE FROM dbo.ProductScaleConfigurations WHERE ProductId=@ProductId;
                  """, ProductParameters(user, productId, request, now), ct);
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.InventoryBalances
                  (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                   InventoryValue,LastProcessingSequence,UpdatedAt)
                SELECT @BusinessId,warehouse.WarehouseId,@ProductId,0,0,0,
                       COALESCE((SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId),0),@Now
                FROM dbo.Warehouses warehouse
                WHERE warehouse.BusinessId=@BusinessId
                  AND NOT EXISTS (
                    SELECT 1 FROM dbo.InventoryBalances balance
                    WHERE balance.BusinessId=@BusinessId
                      AND balance.WarehouseId=warehouse.WarehouseId
                      AND balance.ProductId=@ProductId);
                """, [P("@BusinessId", user.BusinessId), P("@ProductId", productId), P("@Now", now)], ct);
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.ProductLinks SET IsActive=0,UpdatedAt=@Now WHERE BusinessId=@BusinessId AND ChildProductId=@ProductId AND IsActive=1;
                IF @ParentProductId IS NOT NULL
                BEGIN
                  IF EXISTS(SELECT 1 FROM dbo.ProductLinks WHERE BusinessId=@BusinessId AND ChildProductId=@ProductId)
                    UPDATE dbo.ProductLinks SET ParentProductId=@ParentProductId,InventoryFactor=@InventoryFactor,
                      PriceFactor=@PriceFactor,ConversionFactor=@ConversionFactor,SharesInventory=@SharesInventory,
                      SharesPrice=@SharesPrice,AllowsConversion=@AllowsConversion,IsActive=1,UpdatedAt=@Now
                    WHERE BusinessId=@BusinessId AND ChildProductId=@ProductId;
                  ELSE
                    INSERT dbo.ProductLinks(ProductLinkId,BusinessId,ChildProductId,ParentProductId,InventoryFactor,PriceFactor,ConversionFactor,SharesInventory,SharesPrice,AllowsConversion,IsActive,CreatedAt)
                    VALUES(@ProductLinkId,@BusinessId,@ProductId,@ParentProductId,@InventoryFactor,@PriceFactor,@ConversionFactor,@SharesInventory,@SharesPrice,@AllowsConversion,1,@Now);
                END;
                """, [P("@ProductLinkId", ids.NewId()), P("@BusinessId", user.BusinessId), P("@ProductId", productId), P("@ParentProductId", request.Link?.ParentProductId),
                P("@InventoryFactor", request.Link is { SharesInventory: true } ? request.Link.InventoryFactor : null), P("@PriceFactor", request.Link is { SharesPrice: true } ? request.Link.PriceFactor : null),
                P("@ConversionFactor", request.Link is { AllowsConversion: true } ? request.Link.ConversionFactor : null), P("@SharesInventory", request.Link?.SharesInventory ?? false),
                P("@SharesPrice", request.Link?.SharesPrice ?? false), P("@AllowsConversion", request.Link?.AllowsConversion ?? false), P("@Now", now)], ct);


            foreach (var barcode in request.Barcodes.DistinctBy(value => value.Value, StringComparer.OrdinalIgnoreCase))
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.ProductBarcodes
                      (ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
                    VALUES (@Id,@BusinessId,@ProductId,@Value,@Flag,1,@Now);
                    """, [P("@Id", ids.NewId()),
                    P("@BusinessId", user.BusinessId), P("@ProductId", productId), P("@Value", barcode.Value.Trim()),
                    P("@Flag", barcode.IsPrimary), P("@Now", now)], ct);
            }
            foreach (var identifier in request.Identifiers)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.ProductIdentifiers
                      (ProductIdentifierId,BusinessId,ProductId,IdentifierType,Value,IsActive,CreatedAt)
                    VALUES (@Id,@BusinessId,@ProductId,@Type,@Value,1,@Now);
                    """, [P("@Id", ids.NewId()),
                    P("@BusinessId", user.BusinessId), P("@ProductId", productId), P("@Type", identifier.Type.Trim()),
                    P("@Value", identifier.Value.Trim()), P("@Now", now)], ct);
            }
            if (create)
            {
            foreach (var price in request.Prices)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.ProductPrices
                      (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,CostBasisType,
                       CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,
                       RoundingMode,PublishedAt,ValidFrom,IsActive,CreatedAt)
                    VALUES (@Id,@BusinessId,@ProductId,@Amount,@Amount,@Currency,N'Manual',@CostBasis,
                       @TargetMargin,@TargetMargin,N'Margin',1,N'Nearest',@Now,@Now,1,@Now);
                    """, [P("@Id", ids.NewId()), P("@BusinessId", user.BusinessId), P("@ProductId", productId),
                    P("@Amount", price.Amount), P("@CostBasis", price.CostBasisAmount), P("@TargetMargin", price.TargetMarginPercent),
                    P("@Currency", price.CurrencyCode.ToUpperInvariant()), P("@Now", now)], ct);
            }
            }
            else
            {
                var price = request.Prices.Single();
                await ExecuteAsync(connection, transaction, """
                    UPDATE dbo.ProductPrices
                    SET PreparedAmount=@PreparedAmount,CostBasisType=N'Manual',CostBasisAmount=@CostBasis,
                        TargetMarginPercent=@TargetMargin,EffectiveMarginPercent=@TargetMargin,
                        InputMode=@InputMode,RoundingIncrement=@RoundingIncrement,RoundingMode=@RoundingMode
                    WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;
                    IF @@ROWCOUNT=0 THROW 51024,'The product has no active base price.',1;
                    """, [P("@BusinessId", user.BusinessId), P("@ProductId", productId),
                    P("@PreparedAmount", price.PreparedAmount ?? price.Amount), P("@CostBasis", price.CostBasisAmount),
                    P("@TargetMargin", price.TargetMarginPercent), P("@InputMode", price.InputMode),
                    P("@RoundingIncrement", price.RoundingIncrement), P("@RoundingMode", price.RoundingMode)], ct);
            }

            if (request.Link is { SharesPrice: true } linkedCost)
                await SqlLinkedProductCostPreparation.PrepareAsync(
                    connection, transaction, user.BusinessId, linkedCost.ParentProductId,
                    productId, linkedCost.PriceFactor!.Value, ct);

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
            foreach (var supplier in request.Suppliers)
            {
                var supplierId = supplier.SupplierId == Guid.Empty
                    ? ids.NewId()
                    : supplier.SupplierId;
                await ExecuteAsync(connection, transaction, """
                    DECLARE @ExistingSupplierId UNIQUEIDENTIFIER=(SELECT SupplierId FROM dbo.Suppliers WHERE BusinessId=@BusinessId AND Identification=@Identification);
                    IF @ExistingSupplierId IS NOT NULL SET @SupplierId=@ExistingSupplierId;
                    IF EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId<>@BusinessId)
                      THROW 51023, 'The supplier is outside the authenticated scope.', 1;
                    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId)
                    BEGIN
                      INSERT dbo.Parties
                        (PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                      VALUES (@PartyId,@TenantId,N'Organization',@Name,@Name,N'Incomplete',1,@UserId,@Now);
                      INSERT dbo.Suppliers (SupplierId,BusinessId,PartyId,Identification,Name,IsActive,CreatedAt)
                      VALUES (@SupplierId,@BusinessId,@PartyId,@Identification,@Name,1,@Now);
                    END

                    IF @Primary=1
                      UPDATE dbo.SupplierProducts SET IsPrimary=0
                      WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;

                    DECLARE @CurrentSupplierProductId UNIQUEIDENTIFIER=(
                      SELECT SupplierProductId FROM dbo.SupplierProducts WITH (UPDLOCK,HOLDLOCK)
                      WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND SupplierId=@SupplierId);
                    IF @CurrentSupplierProductId IS NULL
                    BEGIN
                      SET @CurrentSupplierProductId=@SupplierProductId;
                      INSERT dbo.SupplierProducts
                        (SupplierProductId,BusinessId,ProductId,SupplierId,SupplierProductCode,PurchasePresentationName,UnitsPerPresentation,IsPrimary,IsActive,CreatedAt)
                      VALUES (@CurrentSupplierProductId,@BusinessId,@ProductId,@SupplierId,@Code,@PresentationName,@UnitsPerPresentation,@Primary,1,@Now);
                    END
                    ELSE
                      UPDATE dbo.SupplierProducts
                      SET SupplierProductCode=@Code,PurchasePresentationName=@PresentationName,UnitsPerPresentation=@UnitsPerPresentation,IsPrimary=@Primary,IsActive=1
                      WHERE SupplierProductId=@CurrentSupplierProductId;

                    IF NOT EXISTS (
                      SELECT 1 FROM dbo.SupplierCostAgreements WITH (UPDLOCK,HOLDLOCK)
                      WHERE SupplierProductId=@CurrentSupplierProductId AND IsActive=1 AND BaseUnitCost=@Cost)
                    BEGIN
                      UPDATE dbo.SupplierCostAgreements
                      SET IsActive=0,ValidUntil=@Now
                      WHERE SupplierProductId=@CurrentSupplierProductId AND IsActive=1;
                      INSERT dbo.SupplierCostAgreements
                        (SupplierCostAgreementId,SupplierProductId,BaseUnitCost,CurrencyCode,ValidFrom,IsActive,CreatedAt)
                      VALUES (@CostId,@CurrentSupplierProductId,@Cost,N'COP',@Now,1,@Now);
                    END
                    """, [P("@SupplierId", supplierId), P("@PartyId", ids.NewId()), P("@SupplierProductId", ids.NewId()),
                    P("@CostId", ids.NewId()),
                    P("@BusinessId", user.BusinessId), P("@TenantId", user.TenantId), P("@UserId", user.UserId), P("@ProductId", productId), P("@Identification", supplier.Identification),
                    P("@Name", supplier.Name), P("@Code", supplier.SupplierProductCode), P("@Primary", supplier.IsPrimary),
                    P("@PresentationName", supplier.PurchasePresentationName.Trim()), P("@UnitsPerPresentation", supplier.UnitsPerPresentation),
                    P("@Cost", supplier.BaseUnitCost), P("@Now", now)], ct);
            }

            await ExecuteAsync(connection, transaction, """
                INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                SELECT @BusinessId,ChildProductId,N'Upsert',@Now
                FROM dbo.ProductLinks
                WHERE BusinessId=@BusinessId AND ParentProductId=@ProductId AND IsActive=1;
                UPDATE dbo.ProductLinks SET IsActive=0,UpdatedAt=@Now
                WHERE BusinessId=@BusinessId AND ParentProductId=@ProductId AND IsActive=1;
                """, [P("@BusinessId", user.BusinessId), P("@ProductId", productId), P("@Now", now)], ct);

            foreach (var child in request.LinkedProducts ?? [])
            {
                await ExecuteAsync(connection, transaction, """
                    IF @ChildId=@ProductId
                      THROW 51024,'A product cannot be linked to itself.',1;
                    IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ChildId AND BusinessId=@BusinessId AND IsActive=1)
                      THROW 51024,'The linked product is outside the business or inactive.',1;
                    IF EXISTS(SELECT 1 FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND ProductId=@ChildId AND QuantityOnHand<>0)
                      THROW 51024,'El producto tiene existencias. Deja su inventario en cero antes de vincularlo.',1;
                    IF @AllowsConversion=1 AND NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND ManageStock=1 AND ConversionMaximumLossPercent IS NOT NULL)
                      THROW 51024,'A convertible family must manage inventory and define a maximum conversion loss.',1;
                    IF EXISTS(SELECT 1 FROM dbo.ProductLinks WHERE BusinessId=@BusinessId AND ParentProductId=@ChildId AND IsActive=1)
                      THROW 51024,'Linked products cannot contain other linked products.',1;
                    IF EXISTS(SELECT 1 FROM dbo.ProductLinks WHERE BusinessId=@BusinessId AND ChildProductId=@ChildId AND ParentProductId<>@ProductId AND IsActive=1)
                      THROW 51024,'The product is already linked to another root product.',1;
                    IF EXISTS(SELECT 1 FROM dbo.ProductLinks WHERE BusinessId=@BusinessId AND ChildProductId=@ChildId)
                      UPDATE dbo.ProductLinks SET ParentProductId=@ProductId,SharesInventory=@SharesInventory,
                          InventoryFactor=@InventoryFactor,SharesPrice=@SharesPrice,PriceFactor=@PriceFactor,
                          AllowsConversion=@AllowsConversion,ConversionFactor=@ConversionFactor,IsActive=1,UpdatedAt=@Now
                      WHERE BusinessId=@BusinessId AND ChildProductId=@ChildId;
                    ELSE
                      INSERT dbo.ProductLinks(ProductLinkId,BusinessId,ChildProductId,ParentProductId,InventoryFactor,PriceFactor,ConversionFactor,SharesInventory,SharesPrice,AllowsConversion,IsActive,CreatedAt)
                      VALUES(@Id,@BusinessId,@ChildId,@ProductId,@InventoryFactor,@PriceFactor,@ConversionFactor,@SharesInventory,@SharesPrice,@AllowsConversion,1,@Now);
                    INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                    VALUES(@BusinessId,@ChildId,N'Upsert',@Now);
                    UPDATE dbo.Products SET ManageStock=CASE WHEN @SharesInventory=1 THEN 0 WHEN @AllowsConversion=1 THEN 1 ELSE ManageStock END,UpdatedAt=@Now
                    WHERE ProductId=@ChildId AND BusinessId=@BusinessId;
                    """, [P("@Id", ids.NewId()), P("@BusinessId", user.BusinessId), P("@ProductId", productId),
                    P("@ChildId", child.ChildProductId), P("@SharesInventory", child.SharesInventory),
                    P("@InventoryFactor", child.SharesInventory ? child.InventoryFactor : null),
                    P("@SharesPrice", child.SharesPrice), P("@PriceFactor", child.SharesPrice ? child.PriceFactor : null),
                    P("@AllowsConversion", child.AllowsConversion), P("@ConversionFactor", child.AllowsConversion ? child.ConversionFactor : null),
                    P("@Now", now)], ct);
                if (child.SharesPrice)
                    await SqlLinkedProductCostPreparation.PrepareAsync(connection, transaction, user.BusinessId,
                        productId, child.ChildProductId, child.PriceFactor!.Value, ct);
            }

            foreach (var alias in request.Aliases ?? [])
            {
                await ExecuteAsync(connection, transaction, """
                    IF EXISTS(SELECT 1 FROM dbo.ProductAliases
                              WHERE BusinessId=@BusinessId AND Scope=0 AND CustomerKey=N''
                                AND NormalizedAlias=@NormalizedAlias AND ProductId<>@ProductId
                                AND Status=1 AND ResolutionMode=1)
                      THROW 51024,'El alias ya resuelve a otro producto del negocio.',1;
                    IF EXISTS(SELECT 1 FROM dbo.ProductAliases
                              WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND Scope=0
                                AND CustomerKey=N'' AND NormalizedAlias=@NormalizedAlias)
                      UPDATE dbo.ProductAliases SET Alias=@Alias,Kind=0,ResolutionMode=1,Source=0,
                        Status=1,UpdatedAt=@Now
                      WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND Scope=0
                        AND CustomerKey=N'' AND NormalizedAlias=@NormalizedAlias;
                    ELSE
                      INSERT dbo.ProductAliases(ProductAliasId,BusinessId,ProductId,Scope,CustomerKey,Alias,
                        NormalizedAlias,Kind,ResolutionMode,Source,Status,UsageCount,CreatedAt)
                      VALUES(@Id,@BusinessId,@ProductId,0,N'',@Alias,@NormalizedAlias,0,1,0,1,0,@Now);
                    """, [P("@Id", ids.NewId()), P("@BusinessId", user.BusinessId), P("@ProductId", productId),
                    P("@Alias", alias.Alias), P("@NormalizedAlias", alias.NormalizedAlias), P("@Now", now)], ct);
            }

            if (request.Images is not null)
            {
                await ExecuteAsync(connection, transaction,
                    "DELETE dbo.ProductImages WHERE BusinessId=@BusinessId AND ProductId=@ProductId;",
                    [P("@BusinessId", user.BusinessId), P("@ProductId", productId)], ct);
                foreach (var image in request.Images)
                {
                    await ExecuteAsync(connection, transaction, """
                        IF @ProductOfferId IS NOT NULL AND NOT EXISTS(
                          SELECT 1 FROM dbo.ProductOffers WHERE ProductOfferId=@ProductOfferId
                            AND BusinessId=@BusinessId AND ProductId=@ProductId)
                          THROW 51024,'La imagen referencia una oferta que no pertenece al producto.',1;
                        INSERT dbo.ProductImages(ProductImageId,ProductId,BusinessId,ProductOfferId,MediaUrl,
                          AltText,DisplayOrder,IsPrimary,IsActive,CreatedAt)
                        VALUES(@Id,@ProductId,@BusinessId,@ProductOfferId,@MediaReference,@AltText,@DisplayOrder,
                          @IsPrimary,1,@Now);
                        """, [P("@Id", image.ProductImageId), P("@ProductId", productId),
                        P("@BusinessId", user.BusinessId), P("@ProductOfferId", image.ProductOfferId),
                        P("@MediaReference", image.MediaReference.Trim()), P("@AltText", image.AltText?.Trim()),
                        P("@DisplayOrder", image.DisplayOrder), P("@IsPrimary", image.IsPrimary), P("@Now", now)], ct);
                }
            }

            await ExecuteAsync(connection, transaction, """
                DECLARE @Change TABLE (CatalogChangeId BIGINT NOT NULL);
                INSERT dbo.CatalogChanges (BusinessId,ProductId,ChangeKind,OccurredAt)
                  OUTPUT inserted.CatalogChangeId INTO @Change
                  VALUES (@BusinessId,@ProductId,N'Upsert',@Now);
                INSERT dbo.PosSynchronizationOutboxMessages
                  (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                SELECT @NotificationId,@BusinessId,N'Catalog',CatalogChangeId,@Now
                FROM @Change;
                """,
                [
                    P("@NotificationId", ids.NewId()),
                    P("@BusinessId", user.BusinessId),
                    P("@ProductId", productId),
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
        SqlConnection connection, SqlTransaction transaction, Guid businessId, Guid productId,
        IEnumerable<string> values, CancellationToken ct)
    {
        foreach (var barcode in values.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await using var command = new SqlCommand("""
                SELECT TOP(1) p.Name
                FROM dbo.ProductBarcodes b WITH (UPDLOCK,HOLDLOCK)
                JOIN dbo.Products p ON p.ProductId=b.ProductId AND p.BusinessId=b.BusinessId
                WHERE b.BusinessId=@BusinessId AND b.Barcode=@Barcode AND b.ProductId<>@ProductId;
                """, connection, transaction);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@ProductId", productId);
            command.Parameters.AddWithValue("@Barcode", barcode);
            var owner = await command.ExecuteScalarAsync(ct) as string;
            if (owner is not null)
                throw new CatalogConflictException($"El código de barras '{barcode}' ya está asignado al producto '{owner}' y no puede reutilizarse.");
        }
    }

    public async Task<ProductDetail?> GetAsync(Guid tenantId, Guid businessId, Guid productId, bool includeCosts, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = ProductSelect + """
             WHERE b.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.ProductId=@ProductId
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
            WHERE b.TenantId=@TenantId AND p.BusinessId=@BusinessId
              AND (@After IS NULL OR COALESCE(p.ProductCode,p.Sku){comparator}@After)
              AND (@Code IS NULL OR COALESCE(p.ProductCode,p.Sku) LIKE @Code+'%')
              AND (@Reference IS NULL OR p.Reference LIKE @Reference+'%')
              AND (@Name IS NULL OR p.Name LIKE '%'+@Name+'%')
              AND (@Active IS NULL OR p.IsActive=@Active)
              AND (@Barcode IS NULL OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes x WHERE x.ProductId=p.ProductId AND x.IsActive=1 AND x.Barcode=@Barcode))
              AND (@SupplierId IS NULL OR EXISTS (SELECT 1 FROM dbo.SupplierProducts sp WHERE sp.ProductId=p.ProductId AND sp.SupplierId=@SupplierId AND sp.IsActive=1))
              AND EXISTS (SELECT 1 FROM dbo.ProductPrices fp WHERE fp.ProductId=p.ProductId AND fp.IsActive=1
                AND (@MinimumPrice IS NULL OR fp.Amount>=@MinimumPrice)
                AND (@MaximumPrice IS NULL OR fp.Amount<=@MaximumPrice))
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
            DECLARE @Change TABLE (CatalogChangeId BIGINT NOT NULL);
            BEGIN TRANSACTION;
            UPDATE dbo.Products SET IsActive=@IsActive,UpdatedAt=@Now,UpdatedByUserId=@UserId
              WHERE ProductId=@ProductId AND BusinessId=@BusinessId;
            IF @@ROWCOUNT=0 BEGIN ROLLBACK; THROW 51010,'Product not found.',1; END;
            INSERT dbo.CatalogChanges (BusinessId,ProductId,ChangeKind,OccurredAt)
              OUTPUT inserted.CatalogChangeId INTO @Change
              VALUES (@BusinessId,@ProductId,CASE WHEN @IsActive=1 THEN N'Upsert' ELSE N'Tombstone' END,@Now);
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            SELECT @NotificationId,@BusinessId,N'Catalog',CatalogChangeId,@Now
            FROM @Change;
            COMMIT;
            """;
        command.Parameters.AddRange([
            P("@NotificationId", ids.NewId()),
            P("@Now", now),
            P("@UserId", user.UserId),
            P("@IsActive", isActive),
            P("@ProductId", productId),
            P("@BusinessId", user.BusinessId)
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
              (CatalogSyncSessionId,DeviceId,BusinessId,HighWaterMark,CreatedAt,ExpiresAt)
            VALUES (@SessionId,@DeviceId,@BusinessId,@High,@Now,DATEADD(hour,2,@Now));
            INSERT dbo.CatalogSyncSessionProducts (CatalogSyncSessionId,ProductId)
            SELECT @SessionId,p.ProductId FROM dbo.Products p
            WHERE p.BusinessId=@BusinessId AND p.ProductCode IS NOT NULL AND p.TaxProfileId IS NOT NULL
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
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items)))).ToLowerInvariant();
        return new CatalogBootstrapPage(sessionId, high, next, next is not null, hash, items);
    }

    public async Task<CatalogDeltaPage> ChangesAsync(
        Guid deviceId, Guid tenantId, Guid businessId, long cursor, int pageSize, CancellationToken ct)
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
            SELECT TOP (@Take) c.CatalogChangeId,c.ChangeKind,p.ProductId,p.ProductCode,p.Reference,p.Name,p.BaseUnitCode,
              t.DianTaxCode,t.Rate,pr.Amount,pr.CurrencyCode,p.IsActive,p.IsWeighable,p.AllowsFractionalSale,
              COALESCE(pr.CostBasisAmount,0),p.ManageStock,
              COALESCE((SELECT Barcode AS [Value] FROM dbo.ProductBarcodes b WHERE b.ProductId=p.ProductId AND b.IsActive=1 FOR JSON PATH),N'[]'),
              COALESCE((SELECT IdentifierType AS [Type],Value FROM dbo.ProductIdentifiers i WHERE i.ProductId=p.ProductId AND i.IsActive=1 FOR JSON PATH),N'[]'),
              s.ScaleCode,s.BarcodePrefix,s.EmbeddedValueType,s.ValueStart,s.ValueLength,s.DecimalPlaces
            FROM dbo.CatalogChanges c
            JOIN dbo.Products p ON p.ProductId=c.ProductId
            JOIN dbo.TaxProfiles t ON t.TaxProfileId=p.TaxProfileId
            JOIN dbo.EnrolledDevices d ON d.DeviceId=@DeviceId AND d.TenantId=@TenantId AND d.IsActive=1
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
            JOIN dbo.ProductPrices pr ON pr.ProductId=p.ProductId AND pr.BusinessId=c.BusinessId AND pr.IsActive=1
            LEFT JOIN dbo.ProductScaleConfigurations s ON s.ProductId=p.ProductId AND s.IsActive=1
            WHERE c.BusinessId=@BusinessId AND c.CatalogChangeId>@Cursor
            ORDER BY c.CatalogChangeId;
            """;
        command.Parameters.AddRange([P("@Take", pageSize + 1), P("@DeviceId", deviceId), P("@TenantId", tenantId),
            P("@BusinessId", businessId), P("@Cursor", cursor)]);
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
            JOIN dbo.Products p ON p.ProductId=@ProductId AND p.BusinessId=b.BusinessId
            LEFT JOIN dbo.ProductLinks link
              ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            LEFT JOIN dbo.InventoryBalances balance WITH (UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=p.BusinessId AND balance.WarehouseId=w.WarehouseId
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
                JOIN dbo.Businesses business
                  ON business.BusinessId=product.BusinessId
                 AND business.TenantId=@TenantId
                 AND business.IsActive=1
                WHERE product.BusinessId=@BusinessId
                  AND product.ProductId=@ProductId
                  AND product.IsActive=1)
              THROW 51012,'The product is not available in the current business.',1;

            ;WITH scopedProducts AS(
                SELECT product.BusinessId,product.ProductId,
                       COALESCE(product.ProductCode,product.Sku,product.Reference,N'') ProductCode
                FROM dbo.Products product
                JOIN dbo.Businesses business
                  ON business.BusinessId=product.BusinessId
                 AND business.TenantId=@TenantId
                 AND business.IsActive=1
                WHERE product.IsActive=1
                  AND product.ManageStock=1
                  AND ((product.BusinessId=@BusinessId AND product.ProductId=@ProductId)
                    OR (@IncludeOtherBusinesses=1
                      AND product.BusinessId<>@BusinessId
                      AND (
                        EXISTS(
                          SELECT 1
                          FROM dbo.ProductBarcodes candidate
                          JOIN dbo.ProductBarcodes origin
                            ON origin.BusinessId=@BusinessId
                           AND origin.ProductId=@ProductId
                           AND origin.IsActive=1
                           AND origin.Barcode=candidate.Barcode
                          WHERE candidate.BusinessId=product.BusinessId
                            AND candidate.ProductId=product.ProductId
                            AND candidate.IsActive=1)
                        OR EXISTS(
                          SELECT 1
                          FROM dbo.ProductIdentifiers candidate
                          JOIN dbo.ProductIdentifiers origin
                            ON origin.BusinessId=@BusinessId
                           AND origin.ProductId=@ProductId
                           AND origin.IsActive=1
                           AND origin.IdentifierType=candidate.IdentifierType
                           AND origin.Value=candidate.Value
                          WHERE candidate.BusinessId=product.BusinessId
                            AND candidate.ProductId=product.ProductId
                            AND candidate.IsActive=1)
                        OR EXISTS(
                          SELECT 1
                          FROM dbo.Products origin
                          JOIN dbo.IntegrationConnections originConnection
                            ON originConnection.IntegrationConnectionId=origin.IntegrationConnectionId
                          JOIN dbo.IntegrationConnections candidateConnection
                            ON candidateConnection.IntegrationConnectionId=product.IntegrationConnectionId
                           AND candidateConnection.Provider=originConnection.Provider
                           AND candidateConnection.Capability=originConnection.Capability
                           AND ISNULL(candidateConnection.AccountIdentifier,N'')=ISNULL(originConnection.AccountIdentifier,N'')
                          WHERE origin.BusinessId=@BusinessId
                            AND origin.ProductId=@ProductId
                            AND origin.ExternalProductId IS NOT NULL
                            AND origin.ExternalProductId=product.ExternalProductId)
                      )))
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
        SELECT p.ProductId,p.BusinessId,COALESCE(p.ProductCode,p.Sku),p.Reference,p.Name,p.IsActive,
          (SELECT Barcode AS [Value] FROM dbo.ProductBarcodes b WHERE b.ProductId=p.ProductId AND b.IsActive=1 FOR JSON PATH),
          (SELECT Amount,CurrencyCode,CostBasisAmount,TargetMarginPercent,PreparedAmount,InputMode,RoundingIncrement,RoundingMode FROM dbo.ProductPrices x WHERE x.ProductId=p.ProductId AND x.IsActive=1 FOR JSON PATH),
          (SELECT s.SupplierId,s.Identification,s.Name,sp.SupplierProductCode,c.BaseUnitCost,sp.IsPrimary,sp.PurchasePresentationName,sp.UnitsPerPresentation
             FROM dbo.SupplierProducts sp JOIN dbo.Suppliers s ON s.SupplierId=sp.SupplierId
             JOIN dbo.SupplierCostAgreements c ON c.SupplierProductId=sp.SupplierProductId AND c.IsActive=1
             WHERE sp.ProductId=p.ProductId AND sp.IsActive=1 FOR JSON PATH),
          p.TaxProfileId,p.PurchaseTaxProfileId,p.PurchaseTaxTreatment,p.Description,p.BaseUnitCode,p.ManageStock,p.IsWeighable
        FROM dbo.Products p
        JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
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
            reader.IsDBNull(10) ? Guid.Empty : reader.GetGuid(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? "EA" : reader.GetString(13),
            reader.GetBoolean(14),
            reader.GetBoolean(15));
    }

    private async Task<List<PosCatalogItem>> PosItemsAsync(
        SqlConnection connection, string predicate, SqlParameter[] parameters, int take,
        Guid sessionId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Take) p.ProductId,p.ProductCode,p.Reference,p.Name,p.BaseUnitCode,t.DianTaxCode,t.Rate,
              pr.Amount,pr.CurrencyCode,p.IsActive,p.IsWeighable,p.AllowsFractionalSale,
              COALESCE(pr.CostBasisAmount,0),p.ManageStock,
              (SELECT Barcode AS [Value] FROM dbo.ProductBarcodes b WHERE b.ProductId=p.ProductId AND b.IsActive=1 FOR JSON PATH),
              (SELECT IdentifierType AS [Type],Value FROM dbo.ProductIdentifiers i WHERE i.ProductId=p.ProductId AND i.IsActive=1 FOR JSON PATH),
              s.ScaleCode,s.BarcodePrefix,s.EmbeddedValueType,s.ValueStart,s.ValueLength,s.DecimalPlaces
            FROM dbo.CatalogSyncSessions ss
            JOIN dbo.CatalogSyncSessionProducts ssp ON ssp.CatalogSyncSessionId=ss.CatalogSyncSessionId
            JOIN dbo.Products p ON p.ProductId=ssp.ProductId
            JOIN dbo.TaxProfiles t ON t.TaxProfileId=p.TaxProfileId
            JOIN dbo.ProductPrices pr ON pr.ProductId=p.ProductId AND pr.BusinessId=ss.BusinessId AND pr.IsActive=1
            LEFT JOIN dbo.ProductScaleConfigurations s ON s.ProductId=p.ProductId AND s.IsActive=1
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
            reader.GetDecimal(offset + 12), reader.GetBoolean(offset + 13));
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

    private static async Task<string> NextProductCodeAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, CancellationToken ct)
    {
        const string sql = """
            DECLARE @Next INT;
            SELECT @Next=COALESCE(MAX(TRY_CONVERT(INT,SUBSTRING(ProductCode,5,10))),0)+1
            FROM dbo.Products WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND ProductCode LIKE N'PRD-%';
            SELECT @Next;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        var next = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        return $"PRD-{next:D6}";
    }
    private static SqlParameter[] ProductParameters(CatalogUserIdentity user, Guid id, SaveProductRequest r, DateTimeOffset now) =>
        [P("@ProductId", id), P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@ProductCode", r.ProductCode.Trim()),
         P("@InitialPrice", r.Prices.Single().Amount),
         P("@Reference", r.Reference), P("@Name", r.Name.Trim()), P("@Description", r.Description), P("@BaseUnitCode", r.BaseUnitCode.Trim()),
         P("@TaxProfileId", r.TaxProfileId), P("@PurchaseTaxProfileId", r.PurchaseTaxProfileId == Guid.Empty ? r.TaxProfileId : r.PurchaseTaxProfileId),
         P("@PurchaseTaxTreatment", r.PurchaseTaxTreatment), P("@ManageInventory", r.ManageInventory), P("@IsWeighable", r.IsWeighable),
         P("@ConversionMaximumLossPercent", r.ConversionMaximumLossPercent),
         P("@ProductCategoryId", r.ProductCategoryId), P("@ProductBrandId", r.ProductBrandId), P("@AllowsFractionalSale", r.AllowsFractionalSale),
         P("@ParentProductId", r.Link?.ParentProductId),
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
