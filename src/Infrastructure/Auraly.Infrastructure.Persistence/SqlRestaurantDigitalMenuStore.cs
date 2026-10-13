using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlRestaurantDigitalMenuStore(
    SqlServerConnectionFactory connections) : IRestaurantDigitalMenuStore
{
    public async Task<RestaurantDigitalMenuConfiguration?> GetConfigurationAsync(
        Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT menu.PublicCode,menu.Title,menu.Introduction,
                   menu.IsPublished,menu.RowVersion
            FROM sales.RestaurantDigitalMenus menu
            JOIN dbo.Businesses business
              ON business.BusinessId=menu.BusinessId
             AND business.TenantId=menu.TenantId
             AND business.PointOfSaleMode=N'RestaurantBar'
            WHERE menu.TenantId=@TenantId
              AND menu.BusinessId=@BusinessId;

            SELECT selected.ProductId,product.Name,
                   COALESCE(product.ProductCode,product.Sku,N''),
                   selected.PublicDescription
            FROM sales.RestaurantDigitalMenuProducts selected
            JOIN dbo.Products product
              ON product.TenantId=selected.TenantId
             AND product.ProductId=selected.ProductId
            WHERE selected.TenantId=@TenantId
              AND selected.BusinessId=@BusinessId
            ORDER BY selected.SortOrder,selected.ProductId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var configuration = ReadConfiguration(reader, []);
        await reader.NextResultAsync(ct);
        return configuration with { Products = await ReadSelectedProductsAsync(reader, ct) };
    }

    public async Task<RestaurantDigitalMenuConfiguration> SaveConfigurationAsync(
        Guid tenantId, Guid businessId, Guid userId,
        SaveRestaurantDigitalMenuRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand("""
            SET NOCOUNT ON;
            IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND TenantId=@TenantId
                AND IsActive=1 AND PointOfSaleMode=N'RestaurantBar')
              THROW 51110,N'La sede no opera como restaurante y bar.',1;

            DECLARE @PriorVersion binary(8);
            SELECT @PriorVersion=RowVersion
            FROM sales.RestaurantDigitalMenus WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            IF @PriorVersion IS NULL
            BEGIN
              IF @ExpectedVersion IS NOT NULL
                THROW 51111,N'El menú cambió. Vuelve a abrirlo.',1;
              INSERT sales.RestaurantDigitalMenus
                (BusinessId,TenantId,PublicCode,Title,Introduction,
                 IsPublished,CreatedAt,UpdatedAt)
              VALUES(@BusinessId,@TenantId,@PublicCode,@Title,
                     @Introduction,@Published,@Now,@Now);
            END
            ELSE
            BEGIN
              IF @ExpectedVersion IS NULL OR @PriorVersion<>@ExpectedVersion
                THROW 51111,N'El menú cambió. Vuelve a abrirlo.',1;
              UPDATE sales.RestaurantDigitalMenus
              SET Title=@Title,Introduction=@Introduction,
                  IsPublished=@Published,UpdatedAt=@Now
              WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            END;

            IF @ProductsJson IS NOT NULL
            BEGIN
              DECLARE @Selected TABLE
              (
                ProductId uniqueidentifier NOT NULL PRIMARY KEY,
                PublicDescription nvarchar(400) NULL,
                SortOrder int NOT NULL
              );
              INSERT @Selected(ProductId,PublicDescription,SortOrder)
              SELECT CONVERT(uniqueidentifier,JSON_VALUE([value],'$.ProductId')),
                     JSON_VALUE([value],'$.PublicDescription'),
                     CONVERT(int,[key])+1
              FROM OPENJSON(@ProductsJson);

              IF EXISTS(
                SELECT 1 FROM @Selected selected
                LEFT JOIN dbo.Products product
                  ON product.TenantId=@TenantId
                 AND product.ProductId=selected.ProductId
                 AND product.IsActive=1 AND product.IsIngredient=0
                LEFT JOIN dbo.ProductCategories category
                  ON category.TenantId=@TenantId
                 AND category.ProductCategoryId=product.ProductCategoryId
                 AND category.IsActive=1 AND category.IsBrowsable=1
                WHERE product.ProductId IS NULL OR category.ProductCategoryId IS NULL
                   OR NOT EXISTS(
                     SELECT 1 FROM dbo.ProductPrices price
                     WHERE price.BusinessId=@BusinessId
                       AND price.ProductId=selected.ProductId
                       AND price.IsActive=1
                       AND price.ValidFrom<=SYSDATETIMEOFFSET()
                       AND (price.ValidUntil IS NULL OR
                            price.ValidUntil>SYSDATETIMEOFFSET())))
                THROW 51112,N'Hay productos sin precio vigente o no aptos para el menú digital.',1;

              DELETE FROM sales.RestaurantDigitalMenuProducts
              WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
              INSERT sales.RestaurantDigitalMenuProducts
                (BusinessId,TenantId,ProductId,PublicDescription,SortOrder,CreatedAt)
              SELECT @BusinessId,@TenantId,ProductId,PublicDescription,SortOrder,@Now
              FROM @Selected;
            END;

            IF @Published=1 AND NOT EXISTS(
              SELECT 1 FROM sales.RestaurantDigitalMenuProducts selected
              JOIN dbo.Products product
                ON product.TenantId=selected.TenantId
               AND product.ProductId=selected.ProductId
               AND product.IsActive=1 AND product.IsIngredient=0
              JOIN dbo.ProductCategories category
                ON category.TenantId=selected.TenantId
               AND category.ProductCategoryId=product.ProductCategoryId
               AND category.IsActive=1 AND category.IsBrowsable=1
              WHERE selected.BusinessId=@BusinessId
                AND selected.TenantId=@TenantId
                AND EXISTS(SELECT 1 FROM dbo.ProductPrices price
                  WHERE price.BusinessId=@BusinessId
                    AND price.ProductId=selected.ProductId
                    AND price.IsActive=1
                    AND price.ValidFrom<=SYSDATETIMEOFFSET()
                    AND (price.ValidUntil IS NULL OR
                         price.ValidUntil>SYSDATETIMEOFFSET())))
              THROW 51113,N'Selecciona al menos un producto antes de publicar el menú.',1;

            INSERT dbo.AuditLogs
              (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
               EntityId,NewValues,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantDigitalMenuSaved',N'RestaurantDigitalMenu',
                   CONVERT(nvarchar(100),@BusinessId),@Audit,@AuditTime);
            SELECT PublicCode,Title,Introduction,IsPublished,RowVersion
            FROM sales.RestaurantDigitalMenus
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;

            SELECT selected.ProductId,product.Name,
                   COALESCE(product.ProductCode,product.Sku,N''),
                   selected.PublicDescription
            FROM sales.RestaurantDigitalMenuProducts selected
            JOIN dbo.Products product
              ON product.TenantId=selected.TenantId
             AND product.ProductId=selected.ProductId
            WHERE selected.BusinessId=@BusinessId
              AND selected.TenantId=@TenantId
            ORDER BY selected.SortOrder,selected.ProductId;
            """, connection, transaction);
        var expected = DecodeVersion(request.RowVersion);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Title", request.Title);
        command.Parameters.Add("@Introduction", SqlDbType.NVarChar, 500)
            .Value = (object?)request.Introduction ?? DBNull.Value;
        command.Parameters.AddWithValue("@Published", request.IsPublished);
        command.Parameters.AddWithValue("@PublicCode",
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
                .ToLowerInvariant());
        command.Parameters.Add("@ExpectedVersion", SqlDbType.Binary, 8)
            .Value = (object?)expected ?? DBNull.Value;
        var now = DateTimeOffset.UtcNow;
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@AuditTime", now.UtcDateTime);
        command.Parameters.Add("@Audit", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(new { request.Title,
                request.IsPublished });
        command.Parameters.Add("@ProductsJson", SqlDbType.NVarChar, -1).Value =
            request.Products is null ? DBNull.Value :
            JsonSerializer.Serialize(request.Products);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("El menú no pudo guardarse.");
            var result = ReadConfiguration(reader, []);
            await reader.NextResultAsync(ct);
            result = result with { Products = await ReadSelectedProductsAsync(reader, ct) };
            await reader.DisposeAsync();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException error) when (error.Number is 51110 or 51112 or 51113)
        {
            throw new RestaurantAccountValidationException(error.Message);
        }
        catch (SqlException error) when (error.Number == 51111)
        {
            throw new RestaurantAccountConflictException(error.Message);
        }
    }

    public async Task<RestaurantDigitalMenuView?> GetPublicAsync(
        string publicCode, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT menu.Title,menu.Introduction,menu.TenantId,menu.BusinessId
            FROM sales.RestaurantDigitalMenus menu
            JOIN dbo.Businesses business
              ON business.BusinessId=menu.BusinessId
             AND business.TenantId=menu.TenantId
             AND business.IsActive=1
             AND business.PointOfSaleMode=N'RestaurantBar'
            WHERE menu.PublicCode=@Code AND menu.IsPublished=1;

            SELECT category.ProductCategoryId,category.Name,
                   COUNT(product.ProductId)
            FROM sales.RestaurantDigitalMenus menu
            JOIN dbo.ProductCategories category
              ON category.TenantId=menu.TenantId
             AND category.IsActive=1 AND category.IsBrowsable=1
            JOIN dbo.Products product
              ON product.TenantId=category.TenantId
             AND product.ProductCategoryId=category.ProductCategoryId
             AND product.IsActive=1 AND product.IsIngredient=0
            JOIN sales.RestaurantDigitalMenuProducts selected
              ON selected.TenantId=menu.TenantId
             AND selected.BusinessId=menu.BusinessId
             AND selected.ProductId=product.ProductId
            WHERE menu.PublicCode=@Code AND menu.IsPublished=1
              AND EXISTS(SELECT 1 FROM dbo.ProductPrices price
                WHERE price.BusinessId=menu.BusinessId
                  AND price.ProductId=product.ProductId
                  AND price.IsActive=1
                  AND price.ValidFrom<=SYSDATETIMEOFFSET()
                  AND (price.ValidUntil IS NULL OR
                       price.ValidUntil>SYSDATETIMEOFFSET()))
            GROUP BY category.ProductCategoryId,category.Name,
                     category.DisplayOrder
            ORDER BY category.DisplayOrder,category.Name;
            """, connection);
        command.Parameters.AddWithValue("@Code", publicCode);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var title = reader.GetString(0);
        var introduction = reader.IsDBNull(1) ? null : reader.GetString(1);
        await reader.NextResultAsync(ct);
        var categories = new List<RestaurantDigitalMenuCategory>();
        while (await reader.ReadAsync(ct))
            categories.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.GetInt32(2)));
        return new(title, introduction, categories);
    }

    public async Task<RestaurantDigitalMenuProductPage?> GetPublicProductsAsync(
        string publicCode, Guid categoryId, int page, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT menu.BusinessId
            FROM sales.RestaurantDigitalMenus menu
            JOIN dbo.Businesses business
              ON business.BusinessId=menu.BusinessId
             AND business.TenantId=menu.TenantId
             AND business.IsActive=1
             AND business.PointOfSaleMode=N'RestaurantBar'
            JOIN dbo.ProductCategories category
              ON category.TenantId=menu.TenantId
             AND category.ProductCategoryId=@CategoryId
             AND category.IsActive=1 AND category.IsBrowsable=1
            WHERE menu.PublicCode=@Code AND menu.IsPublished=1;

            SELECT product.ProductId,product.Name,
                   selected.PublicDescription,price.Amount,
                   price.CurrencyCode,image.MediaUrl
            FROM sales.RestaurantDigitalMenus menu
            JOIN sales.RestaurantDigitalMenuProducts selected
              ON selected.TenantId=menu.TenantId
             AND selected.BusinessId=menu.BusinessId
            JOIN dbo.Products product
              ON product.TenantId=menu.TenantId
             AND product.ProductId=selected.ProductId
             AND product.ProductCategoryId=@CategoryId
             AND product.IsActive=1 AND product.IsIngredient=0
            CROSS APPLY(
              SELECT TOP(1) Amount,CurrencyCode
              FROM dbo.ProductPrices price
              WHERE price.BusinessId=menu.BusinessId
                AND price.ProductId=product.ProductId
                AND price.IsActive=1
                AND price.ValidFrom<=SYSDATETIMEOFFSET()
                AND (price.ValidUntil IS NULL OR
                     price.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY price.ValidFrom DESC,price.ProductPriceId
            ) price
            OUTER APPLY(
              SELECT TOP(1) MediaUrl
              FROM dbo.ProductImages image
              WHERE image.TenantId=menu.TenantId
                AND image.ProductId=product.ProductId
                AND image.ProductOfferId IS NULL
                AND image.IsActive=1
              ORDER BY image.IsPrimary DESC,image.DisplayOrder,
                       image.ProductImageId
            ) image
            WHERE menu.PublicCode=@Code AND menu.IsPublished=1
            ORDER BY selected.SortOrder,product.Name,product.ProductId
            OFFSET @Skip ROWS FETCH NEXT 25 ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@Code", publicCode);
        command.Parameters.AddWithValue("@CategoryId", categoryId);
        command.Parameters.AddWithValue("@Skip", (page - 1) * 24);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        await reader.NextResultAsync(ct);
        var items = new List<RestaurantDigitalMenuProduct>(25);
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        var hasMore = items.Count > 24;
        if (hasMore) items.RemoveAt(24);
        return new(items, hasMore);
    }

    public async Task<RestaurantDigitalMenuView?> GetPreviewAsync(
        Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT COALESCE(menu.Title,business.Name),menu.Introduction
            FROM dbo.Businesses business
            LEFT JOIN sales.RestaurantDigitalMenus menu
              ON menu.TenantId=business.TenantId
             AND menu.BusinessId=business.BusinessId
            WHERE business.TenantId=@TenantId
              AND business.BusinessId=@BusinessId
              AND business.IsActive=1
              AND business.PointOfSaleMode=N'RestaurantBar';

            SELECT category.ProductCategoryId,category.Name,
                   COUNT(product.ProductId)
            FROM dbo.ProductCategories category
            JOIN dbo.Products product
              ON product.TenantId=category.TenantId
             AND product.ProductCategoryId=category.ProductCategoryId
             AND product.IsActive=1 AND product.IsIngredient=0
            JOIN sales.RestaurantDigitalMenuProducts selected
              ON selected.TenantId=@TenantId
             AND selected.BusinessId=@BusinessId
             AND selected.ProductId=product.ProductId
            WHERE category.TenantId=@TenantId
              AND category.IsActive=1 AND category.IsBrowsable=1
              AND EXISTS(SELECT 1 FROM dbo.ProductPrices price
                WHERE price.BusinessId=@BusinessId
                  AND price.ProductId=product.ProductId
                  AND price.IsActive=1
                  AND price.ValidFrom<=SYSDATETIMEOFFSET()
                  AND (price.ValidUntil IS NULL OR
                       price.ValidUntil>SYSDATETIMEOFFSET()))
            GROUP BY category.ProductCategoryId,category.Name,
                     category.DisplayOrder
            ORDER BY category.DisplayOrder,category.Name;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var title = reader.GetString(0);
        var introduction = reader.IsDBNull(1) ? null : reader.GetString(1);
        await reader.NextResultAsync(ct);
        var categories = new List<RestaurantDigitalMenuCategory>();
        while (await reader.ReadAsync(ct))
            categories.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.GetInt32(2)));
        return new(title, introduction, categories);
    }

    public async Task<RestaurantDigitalMenuProductPage?> GetPreviewProductsAsync(
        Guid tenantId, Guid businessId, Guid categoryId, int page,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT business.BusinessId
            FROM dbo.Businesses business
            JOIN dbo.ProductCategories category
              ON category.TenantId=business.TenantId
             AND category.ProductCategoryId=@CategoryId
             AND category.IsActive=1 AND category.IsBrowsable=1
            WHERE business.TenantId=@TenantId
              AND business.BusinessId=@BusinessId
              AND business.IsActive=1
              AND business.PointOfSaleMode=N'RestaurantBar';

            SELECT product.ProductId,product.Name,
                   selected.PublicDescription,price.Amount,
                   price.CurrencyCode,image.MediaUrl
            FROM sales.RestaurantDigitalMenuProducts selected
            JOIN dbo.Products product
              ON product.TenantId=selected.TenantId
             AND product.ProductId=selected.ProductId
            CROSS APPLY(
              SELECT TOP(1) Amount,CurrencyCode
              FROM dbo.ProductPrices price
              WHERE price.BusinessId=@BusinessId
                AND price.ProductId=product.ProductId
                AND price.IsActive=1
                AND price.ValidFrom<=SYSDATETIMEOFFSET()
                AND (price.ValidUntil IS NULL OR
                     price.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY price.ValidFrom DESC,price.ProductPriceId
            ) price
            OUTER APPLY(
              SELECT TOP(1) MediaUrl
              FROM dbo.ProductImages image
              WHERE image.TenantId=@TenantId
                AND image.ProductId=product.ProductId
                AND image.ProductOfferId IS NULL
                AND image.IsActive=1
              ORDER BY image.IsPrimary DESC,image.DisplayOrder,
                       image.ProductImageId
            ) image
            WHERE product.TenantId=@TenantId
              AND selected.BusinessId=@BusinessId
              AND product.ProductCategoryId=@CategoryId
              AND product.IsActive=1 AND product.IsIngredient=0
            ORDER BY selected.SortOrder,product.Name,product.ProductId
            OFFSET @Skip ROWS FETCH NEXT 25 ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@CategoryId", categoryId);
        command.Parameters.AddWithValue("@Skip", (page - 1) * 24);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        await reader.NextResultAsync(ct);
        var items = new List<RestaurantDigitalMenuProduct>(25);
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        var hasMore = items.Count > 24;
        if (hasMore) items.RemoveAt(24);
        return new(items, hasMore);
    }

    private static async Task<IReadOnlyList<RestaurantDigitalMenuSelectedProduct>>
        ReadSelectedProductsAsync(SqlDataReader reader, CancellationToken ct)
    {
        var products = new List<RestaurantDigitalMenuSelectedProduct>();
        while (await reader.ReadAsync(ct))
            products.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return products;
    }

    private static RestaurantDigitalMenuConfiguration ReadConfiguration(
        SqlDataReader reader,
        IReadOnlyList<RestaurantDigitalMenuSelectedProduct> products) =>
        new(reader.GetString(0),reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),reader.GetBoolean(3),
            Convert.ToBase64String((byte[])reader[4]), products);

    private static byte[]? DecodeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException) { }
        throw new RestaurantAccountValidationException(
            "La versión del menú no es válida.");
    }
}
