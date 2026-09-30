using System.Data;
using System.Text.Json;
using Auraly.Application.Catalog;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlProductMerchandisingStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids) : IProductMerchandisingStore
{
    public async Task<IReadOnlyList<ProductBrandSummary>> ListBrandsAsync(CatalogUserIdentity user, bool includeInactive, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT x.ProductBrandId,x.Name,x.IsActive
            FROM dbo.ProductBrands x
            WHERE x.TenantId=@TenantId AND (@IncludeInactive=1 OR x.IsActive=1)
            ORDER BY x.Name;
            """;
        command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@IncludeInactive", includeInactive)]);
        var result = new List<ProductBrandSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2)));
        return result;
    }

    public async Task<ProductBrandSummary> SaveBrandAsync(CatalogUserIdentity user, Guid? id, SaveProductBrandRequest request, DateTimeOffset now, CancellationToken ct)
    {
        var brandId = id ?? ids.NewId();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51021,'The business is outside the authenticated tenant.',1;
            IF @Create=1
              INSERT dbo.ProductBrands(ProductBrandId,TenantId,Name,IsActive,CreatedAt) VALUES(@Id,@TenantId,@Name,@Active,@Now);
            ELSE
            BEGIN
              UPDATE dbo.ProductBrands SET Name=@Name,IsActive=@Active,UpdatedAt=@Now WHERE ProductBrandId=@Id AND TenantId=@TenantId;
              IF @@ROWCOUNT=0 THROW 51010,'Product brand was not found.',1;
            END
            SELECT ProductBrandId,Name,IsActive FROM dbo.ProductBrands WHERE ProductBrandId=@Id;
            """;
        command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@Id", brandId), P("@Create", id is null), P("@Name", request.Name), P("@Active", request.IsActive), P("@Now", now)]);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new CatalogConflictException("A product brand with this name already exists."); }
    }

    public async Task<IReadOnlyList<ProductUnitSummary>> ListUnitsAsync(CatalogUserIdentity user, bool includeInactive, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT x.ProductUnitId,x.Code,x.Name,x.Symbol,x.AllowsFractionalQuantity,x.DecimalPlaces,x.IsActive
            FROM dbo.ProductUnits x
            WHERE x.TenantId=@TenantId AND (@IncludeInactive=1 OR x.IsActive=1)
            ORDER BY x.Name;
            """;
        command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@IncludeInactive", includeInactive)]);
        var result = new List<ProductUnitSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetByte(5), reader.GetBoolean(6)));
        return result;
    }

    public async Task<ProductUnitSummary> SaveUnitAsync(CatalogUserIdentity user, Guid? id, SaveProductUnitRequest request, DateTimeOffset now, CancellationToken ct)
    {
        var unitId = id ?? ids.NewId();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51021,'The business is outside the authenticated tenant.',1;
            IF @Create=1
              INSERT dbo.ProductUnits(ProductUnitId,TenantId,Code,Name,Symbol,AllowsFractionalQuantity,DecimalPlaces,IsActive,CreatedAt)
              VALUES(@Id,@TenantId,@Code,@Name,@Symbol,@Fractional,@Decimals,@Active,@Now);
            ELSE
            BEGIN
              UPDATE dbo.ProductUnits SET Code=@Code,Name=@Name,Symbol=@Symbol,AllowsFractionalQuantity=@Fractional,DecimalPlaces=@Decimals,IsActive=@Active,UpdatedAt=@Now
              WHERE ProductUnitId=@Id AND TenantId=@TenantId;
              IF @@ROWCOUNT=0 THROW 51010,'Sale unit was not found.',1;
            END
            SELECT ProductUnitId,Code,Name,Symbol,AllowsFractionalQuantity,DecimalPlaces,IsActive FROM dbo.ProductUnits WHERE ProductUnitId=@Id;
            """;
        command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@Id", unitId), P("@Create", id is null), P("@Code", request.Code), P("@Name", request.Name), P("@Symbol", request.Symbol), P("@Fractional", request.AllowsFractionalQuantity), P("@Decimals", request.DecimalPlaces), P("@Active", request.IsActive), P("@Now", now)]);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetByte(5), reader.GetBoolean(6));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new CatalogConflictException("A sale unit with this code already exists."); }
    }

    public async Task<ProductMerchandisingConfiguration?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectConfiguration;
        command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@ProductId", productId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<ProductMerchandisingConfiguration> SaveAsync(CatalogUserIdentity user, Guid productId, SaveProductMerchandisingRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await EnsureBarcodesAvailableAsync(connection, transaction, user.TenantId, productId, request.Barcodes.Select(value => value.Value), ct);
            await using (var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Products p WHERE p.ProductId=@ProductId AND p.TenantId=@TenantId
                  AND EXISTS(SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId))
                  THROW 51010,'Product was not found.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.ProductUnits WHERE TenantId=@TenantId AND Code=@UnitCode AND IsActive=1)
                  THROW 51020,'The selected sale unit is invalid.',1;
                IF @CategoryId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.ProductCategories WHERE ProductCategoryId=@CategoryId AND TenantId=@TenantId AND IsActive=1)
                  THROW 51020,'The selected product category is invalid.',1;
                IF @BrandId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.ProductBrands WHERE ProductBrandId=@BrandId AND TenantId=@TenantId AND IsActive=1)
                  THROW 51020,'The selected product brand is invalid.',1;
                UPDATE dbo.Products SET ProductCategoryId=@CategoryId,CategoryName=(SELECT Name FROM dbo.ProductCategories WHERE ProductCategoryId=@CategoryId),ProductBrandId=@BrandId,BaseUnitCode=@UnitCode,ManageStock=@ManageInventory,IsGenericProduct=@IsGenericProduct,UnitGrossWeightKg=@UnitGrossWeightKg,ConversionMaximumLossPercent=@MaximumLoss,AllowsFractionalSale=@Fractional,IsWeighable=@Weighable,UpdatedAt=@Now,UpdatedByUserId=@UserId WHERE ProductId=@ProductId;
                DELETE dbo.ProductBarcodes WHERE ProductId=@ProductId AND TenantId=@TenantId;
                DELETE dbo.ProductScaleConfigurations WHERE ProductId=@ProductId;
                """, connection, transaction))
            {
                command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@UserId", user.UserId), P("@ProductId", productId), P("@CategoryId", request.ProductCategoryId), P("@BrandId", request.ProductBrandId), P("@UnitCode", request.BaseUnitCode), P("@ManageInventory", request.ManageInventory), P("@IsGenericProduct", request.IsGenericProduct), P("@UnitGrossWeightKg", request.UnitGrossWeightKg), P("@MaximumLoss", request.ConversionMaximumLossPercent), P("@Fractional", request.AllowsFractionalSale), P("@Weighable", request.IsWeighable), P("@Now", now)]);
                await command.ExecuteNonQueryAsync(ct);
            }

            await ExecuteAsync(connection, transaction, """
                INSERT dbo.ProductBarcodes(ProductBarcodeId,TenantId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
                SELECT NEWID(),@TenantId,@ProductId,barcode.Value,barcode.IsPrimary,1,@Now
                FROM OPENJSON(@Barcodes) WITH (Value NVARCHAR(64) '$.Value',IsPrimary BIT '$.IsPrimary') barcode;
                """, [P("@TenantId", user.TenantId), P("@ProductId", productId),
                    P("@Barcodes", JsonSerializer.Serialize(request.Barcodes.Select(barcode => new { Value = barcode.Value.Trim(), barcode.IsPrimary }))),
                    P("@Now", now)], ct);

            if (request.Scale is { } scale)
                await ExecuteAsync(connection, transaction, "INSERT dbo.ProductScaleConfigurations(ProductId,ScaleCode,BarcodePrefix,EmbeddedValueType,ValueStart,ValueLength,DecimalPlaces,IsActive) VALUES(@ProductId,@Code,@Prefix,@Type,@Start,@Length,@Decimals,1);", [P("@ProductId", productId), P("@Code", scale.ScaleCode), P("@Prefix", scale.BarcodePrefix), P("@Type", scale.EmbeddedValueType), P("@Start", scale.ValueStart), P("@Length", scale.ValueLength), P("@Decimals", scale.DecimalPlaces)], ct);

            await ExecuteAsync(connection, transaction, "UPDATE dbo.ProductLinks SET IsActive=0,UpdatedAt=@Now WHERE TenantId=@TenantId AND ChildProductId=@ProductId AND IsActive=1;", [P("@TenantId", user.TenantId), P("@ProductId", productId), P("@Now", now)], ct);
            if (request.Link is { } link)
            {
                await ExecuteAsync(connection, transaction, """
                    IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ParentId AND TenantId=@TenantId AND IsActive=1)
                      THROW 51020,'The parent product is outside the business or inactive.',1;
                    IF @SharesInventory=1 AND EXISTS(SELECT 1 FROM dbo.InventoryBalances balance JOIN dbo.Businesses businessValue ON businessValue.BusinessId=balance.BusinessId WHERE businessValue.TenantId=@TenantId AND balance.ProductId=@ProductId AND balance.QuantityOnHand<>0)
                      THROW 51020,'El producto tiene existencias. Deja su inventario en cero antes de vincularlo.',1;
                    IF @AllowsConversion=1 AND NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ParentId AND TenantId=@TenantId AND ManageStock=1 AND ConversionMaximumLossPercent IS NOT NULL)
                      THROW 51020,'The parent product must manage inventory and define a maximum conversion loss.',1;
                    IF EXISTS(SELECT 1 FROM dbo.ProductLinks WHERE TenantId=@TenantId AND ChildProductId=@ParentId AND IsActive=1)
                       OR EXISTS(SELECT 1 FROM dbo.ProductLinks WHERE TenantId=@TenantId AND ParentProductId=@ProductId AND IsActive=1)
                      THROW 51020,'Linked products must point directly to one root product; chains and cycles are not allowed.',1;
                    IF EXISTS(SELECT 1 FROM dbo.ProductLinks WHERE TenantId=@TenantId AND ChildProductId=@ProductId)
                      UPDATE dbo.ProductLinks SET ParentProductId=@ParentId,SharesInventory=@SharesInventory,InventoryFactor=@InventoryFactor,SharesPrice=@SharesPrice,PriceFactor=@PriceFactor,AllowsConversion=@AllowsConversion,ConversionFactor=@ConversionFactor,IsActive=1,UpdatedAt=@Now WHERE TenantId=@TenantId AND ChildProductId=@ProductId;
                    ELSE
                      INSERT dbo.ProductLinks(ProductLinkId,TenantId,ChildProductId,ParentProductId,InventoryFactor,PriceFactor,ConversionFactor,SharesInventory,SharesPrice,AllowsConversion,IsActive,CreatedAt) VALUES(@Id,@TenantId,@ProductId,@ParentId,@InventoryFactor,@PriceFactor,@ConversionFactor,@SharesInventory,@SharesPrice,@AllowsConversion,1,@Now);
                    UPDATE dbo.Products SET ManageStock=CASE WHEN @SharesInventory=1 THEN 0 WHEN @AllowsConversion=1 THEN 1 ELSE ManageStock END,UpdatedAt=@Now WHERE ProductId=@ProductId AND TenantId=@TenantId;
                    """, [P("@Id", ids.NewId()), P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@ProductId", productId), P("@ParentId", link.ParentProductId), P("@SharesInventory", link.SharesInventory), P("@InventoryFactor", link.SharesInventory ? link.InventoryFactor : null), P("@SharesPrice", link.SharesPrice), P("@PriceFactor", link.SharesPrice ? link.PriceFactor : null), P("@AllowsConversion", link.AllowsConversion), P("@ConversionFactor", link.AllowsConversion ? link.ConversionFactor : null), P("@Now", now)], ct);
                if (link.SharesPrice)
                    await SqlLinkedProductCostPreparation.PrepareAsync(connection, transaction, user.BusinessId,
                        link.ParentProductId, productId, user.UserId, now, ct);
            }

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
                request.LinkedProducts, now, false, 51020, ct);
            affectedCatalogProducts.UnionWith(
                request.LinkedProducts.Select(child => child.ChildProductId));
            if (request.LinkedProducts.Any(child => child.SharesPrice))
                await SqlLinkedProductCostPreparation.PrepareFamilyAsync(
                    connection, transaction, user.BusinessId, productId, null, null,
                    user.UserId, now, ct);

            await ExecuteAsync(connection, transaction, """
                DECLARE @Change TABLE(BusinessId UNIQUEIDENTIFIER NOT NULL,CatalogChangeId BIGINT NOT NULL);
                INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                  OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @Change
                  SELECT business.BusinessId,affected.ProductId,N'Upsert',@Now
                  FROM dbo.Businesses business
                  CROSS JOIN (SELECT @ProductId ProductId
                              UNION SELECT TRY_CONVERT(UNIQUEIDENTIFIER,[value]) FROM OPENJSON(@AffectedProductIds)) affected
                  WHERE business.TenantId=@TenantId AND business.IsActive=1;
                INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                  SELECT NEWID(),BusinessId,N'Catalog',CatalogChangeId,@Now FROM @Change;
                """, [P("@TenantId", user.TenantId), P("@ProductId", productId),
                    P("@AffectedProductIds", JsonSerializer.Serialize(affectedCatalogProducts)), P("@Now", now)], ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException ex) when (ex.Number is 51020 or 51021 or 51022 or 51024)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogValidationException(ex.Message);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogConflictException("A barcode is already assigned to another product.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }

        return (await GetAsync(user, productId, ct))!;
    }

    private static async Task EnsureBarcodesAvailableAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid productId,
        IEnumerable<string> values, CancellationToken ct)
    {
        var barcodes = values.Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (barcodes.Length == 0) return;
        await using var command = new SqlCommand("""
            SELECT TOP(1) requested.Barcode,p.Name
            FROM OPENJSON(@Barcodes) WITH (Barcode NVARCHAR(64) '$') requested
            JOIN dbo.ProductBarcodes b WITH (UPDLOCK,HOLDLOCK)
              ON b.TenantId=@TenantId AND b.Barcode=requested.Barcode AND b.ProductId<>@ProductId
            JOIN dbo.Products p ON p.ProductId=b.ProductId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@Barcodes", JsonSerializer.Serialize(barcodes));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            throw new CatalogConflictException($"El código de barras '{reader.GetString(0)}' ya está asignado al producto '{reader.GetString(1)}' y no puede reutilizarse.");
    }

    private const string SelectConfiguration = """
        SELECT p.ProductId,p.ProductCategoryId,p.ProductBrandId,COALESCE(p.BaseUnitCode,N'EA'),p.ManageStock,p.AllowsFractionalSale,p.IsWeighable,
          s.ScaleCode,s.BarcodePrefix,s.EmbeddedValueType,s.ValueStart,s.ValueLength,s.DecimalPlaces,
          (SELECT Barcode AS Value,IsPrimary FROM dbo.ProductBarcodes WHERE ProductId=p.ProductId AND TenantId=@TenantId AND IsActive=1 ORDER BY IsPrimary DESC,Barcode FOR JSON PATH),
          l.ParentProductId,COALESCE(parent.ProductCode,parent.Sku),parent.Name,l.SharesInventory,l.InventoryFactor,l.SharesPrice,l.PriceFactor,
          (SELECT child.ProductId AS ChildProductId,COALESCE(child.ProductCode,child.Sku) AS ChildProductCode,child.Name AS ChildProductName,
                  links.SharesInventory,links.InventoryFactor,links.SharesPrice,links.PriceFactor,links.AllowsConversion,links.ConversionFactor
             FROM dbo.ProductLinks links JOIN dbo.Products child ON child.ProductId=links.ChildProductId AND child.TenantId=p.TenantId WHERE links.TenantId=@TenantId AND links.ParentProductId=p.ProductId AND links.IsActive=1 ORDER BY child.Name FOR JSON PATH)
          ,l.AllowsConversion,l.ConversionFactor,p.ConversionMaximumLossPercent,p.UnitGrossWeightKg,p.IsGenericProduct
        FROM dbo.Products p
        LEFT JOIN dbo.ProductScaleConfigurations s ON s.ProductId=p.ProductId AND s.IsActive=1
        LEFT JOIN dbo.ProductLinks l ON l.ChildProductId=p.ProductId AND l.TenantId=@TenantId AND l.IsActive=1
        LEFT JOIN dbo.Products parent ON parent.ProductId=l.ParentProductId AND parent.TenantId=p.TenantId
        WHERE p.ProductId=@ProductId AND p.TenantId=@TenantId
          AND EXISTS(SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId);
        """;

    private static ProductMerchandisingConfiguration Read(SqlDataReader r)
    {
        var scale = r.IsDBNull(7) ? null : new ScaleConfigurationInput(r.GetString(7), r.GetString(8), r.GetString(9), r.GetInt32(10), r.GetInt32(11), r.GetInt32(12));
        var barcodes = r.IsDBNull(13) ? [] : JsonSerializer.Deserialize<ProductBarcodeInput[]>(r.GetString(13)) ?? [];
        var link = r.IsDBNull(14) ? null : new ProductLinkDetail(r.GetGuid(14), r.IsDBNull(15) ? string.Empty : r.GetString(15), r.GetString(16), r.GetBoolean(17), r.IsDBNull(18) ? null : r.GetDecimal(18), r.GetBoolean(19), r.IsDBNull(20) ? null : r.GetDecimal(20), r.GetBoolean(22), r.IsDBNull(23) ? null : r.GetDecimal(23));
        var linkedProducts = r.IsDBNull(21) ? [] : JsonSerializer.Deserialize<LinkedProductDetail[]>(r.GetString(21)) ?? [];
        return new(r.GetGuid(0), r.IsDBNull(1) ? null : r.GetGuid(1), r.IsDBNull(2) ? null : r.GetGuid(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5), r.GetBoolean(6), scale, barcodes, link, linkedProducts, r.IsDBNull(24) ? null : r.GetDecimal(24), r.IsDBNull(25) ? null : r.GetDecimal(25), r.GetBoolean(26));
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, SqlParameter[] parameters, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
}
