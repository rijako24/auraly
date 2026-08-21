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
                """, ProductParameters(user, productId, request, now), ct);
            await ExecuteAsync(connection, transaction, create
                ? """
                  INSERT dbo.Products
                    (ProductId,BusinessId,ProductCode,Reference,Sku,Name,Description,ProductCategoryId,CategoryName,ProductBrandId,BaseUnitCode,TaxProfileId,
                     PurchaseTaxProfileId,PurchaseTaxTreatment,ManageStock,AllowsFractionalSale,IsWeighable,IsActive,Source,UnitPrice,Currency,CreatedAt,UpdatedAt,CreatedByUserId,UpdatedByUserId)
                  VALUES
                    (@ProductId,@BusinessId,@ProductCode,@Reference,@Reference,@Name,@Description,@ProductCategoryId,(SELECT Name FROM dbo.ProductCategories WHERE ProductCategoryId=@ProductCategoryId),@ProductBrandId,@BaseUnitCode,@TaxProfileId,
                     @PurchaseTaxProfileId,@PurchaseTaxTreatment,@ManageInventory,@AllowsFractionalSale,@IsWeighable,1,0,@InitialPrice,N'COP',@Now,NULL,@UserId,NULL);
                  """
                : """
                  UPDATE dbo.Products SET ProductCode=@ProductCode,Reference=@Reference,Sku=@Reference,Name=@Name,
                    Description=@Description,ProductCategoryId=@ProductCategoryId,CategoryName=(SELECT Name FROM dbo.ProductCategories WHERE ProductCategoryId=@ProductCategoryId),ProductBrandId=@ProductBrandId,BaseUnitCode=@BaseUnitCode,TaxProfileId=@TaxProfileId,
                    PurchaseTaxProfileId=@PurchaseTaxProfileId,PurchaseTaxTreatment=@PurchaseTaxTreatment,ManageStock=@ManageInventory,AllowsFractionalSale=@AllowsFractionalSale,IsWeighable=@IsWeighable,UpdatedAt=@Now,UpdatedByUserId=@UserId
                  WHERE ProductId=@ProductId AND BusinessId=@BusinessId;
                  IF @@ROWCOUNT=0 THROW 51010, 'Product was not found in the authenticated scope.', 1;
                  DELETE FROM dbo.ProductBarcodes WHERE ProductId=@ProductId;
                  DELETE FROM dbo.ProductIdentifiers WHERE ProductId=@ProductId;
                  DELETE FROM dbo.ProductScaleConfigurations WHERE ProductId=@ProductId;
                  """, ProductParameters(user, productId, request, now), ct);
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.ProductLinks SET IsActive=0,UpdatedAt=@Now WHERE BusinessId=@BusinessId AND ChildProductId=@ProductId AND IsActive=1;
                IF @ParentProductId IS NOT NULL
                  INSERT dbo.ProductLinks(ProductLinkId,BusinessId,ChildProductId,ParentProductId,InventoryFactor,PriceFactor,SharesInventory,SharesPrice,IsActive,CreatedAt)
                  VALUES(@ProductLinkId,@BusinessId,@ProductId,@ParentProductId,@InventoryFactor,@PriceFactor,@SharesInventory,@SharesPrice,1,@Now);
                """, [P("@ProductLinkId", ids.NewId()), P("@BusinessId", user.BusinessId), P("@ProductId", productId), P("@ParentProductId", request.Link?.ParentProductId),
                P("@InventoryFactor", request.Link is { SharesInventory: true } ? request.Link.InventoryFactor : null), P("@PriceFactor", request.Link is { SharesPrice: true } ? request.Link.PriceFactor : null),
                P("@SharesInventory", request.Link?.SharesInventory ?? false), P("@SharesPrice", request.Link?.SharesPrice ?? false), P("@Now", now)], ct);


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
              t.DianTaxCode,t.Rate,pr.Amount,pr.CurrencyCode,p.IsActive,
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
              ISNULL((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m
                WHERE m.BusinessId=@BusinessId AND m.WarehouseId=w.WarehouseId
                  AND m.ProductId=COALESCE(link.ParentProductId,p.ProductId)),0) / COALESCE(NULLIF(link.InventoryFactor,0),1)
            FROM dbo.EnrolledDevices d
            JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
              AND b.TenantId=d.TenantId AND b.IsActive=1
            JOIN dbo.Warehouses w ON w.WarehouseId=@WarehouseId AND w.IsActive=1 AND w.UseForSales=1
              AND w.BusinessId=b.BusinessId AND w.IsActive=1
            JOIN dbo.Products p ON p.ProductId=@ProductId AND p.BusinessId=b.BusinessId
            LEFT JOIN dbo.ProductLinks link
              ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
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

    private const string ProductSelect = """
        SELECT p.ProductId,p.BusinessId,COALESCE(p.ProductCode,p.Sku),p.Reference,p.Name,p.IsActive,
          (SELECT Barcode AS [Value] FROM dbo.ProductBarcodes b WHERE b.ProductId=p.ProductId AND b.IsActive=1 FOR JSON PATH),
          (SELECT Amount,CurrencyCode,CostBasisAmount,TargetMarginPercent FROM dbo.ProductPrices x WHERE x.ProductId=p.ProductId AND x.IsActive=1 FOR JSON PATH),
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
              pr.Amount,pr.CurrencyCode,p.IsActive,
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
        ScaleConfigurationInput? scale = reader.IsDBNull(offset + 12) ? null :
            new(reader.GetString(offset + 12), reader.GetString(offset + 13), reader.GetString(offset + 14),
                reader.GetInt32(offset + 15), reader.GetInt32(offset + 16), reader.GetInt32(offset + 17));
        return new(reader.GetGuid(offset), reader.GetString(offset + 1), reader.IsDBNull(offset + 2) ? null : reader.GetString(offset + 2),
            reader.GetString(offset + 3), reader.GetString(offset + 4), reader.GetString(offset + 5), reader.GetDecimal(offset + 6),
            reader.GetDecimal(offset + 7), reader.GetString(offset + 8), reader.GetBoolean(offset + 9), scale,
            DeserializeArray<BarcodeJson>(reader, offset + 10).Select(value => value.Value).ToArray(),
            DeserializeArray<ProductIdentifierInput>(reader, offset + 11));
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
         P("@ProductCategoryId", r.ProductCategoryId), P("@ProductBrandId", r.ProductBrandId), P("@AllowsFractionalSale", r.AllowsFractionalSale),
         P("@ParentProductId", r.Link?.ParentProductId),
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
