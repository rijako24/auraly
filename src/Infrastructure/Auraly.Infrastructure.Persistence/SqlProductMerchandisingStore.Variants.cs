using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlProductMerchandisingStore
{
    public async Task<IReadOnlyList<PosCatalogCategory>> PosCategoriesAsync(CatalogUserIdentity user, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT ProductCategoryId,Name FROM dbo.ProductCategories
            WHERE TenantId=@TenantId AND IsActive=1 AND IsBrowsable=1
              AND EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
            ORDER BY DisplayOrder,Name,ProductCategoryId;
            """, connection);
        command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId)]);
        var result = new List<PosCatalogCategory>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }

    public async Task<IReadOnlyList<ProductVariantOption>> ListVariantOptionsAsync(
        CatalogUserIdentity user, bool colors, bool includeInactive, CancellationToken ct)
    {
        var (table, key) = VariantTable(colors);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand($"""
            SELECT {key},Code,Name,IsActive,SortOrder FROM {table}
            WHERE TenantId=@TenantId AND (@IncludeInactive=1 OR IsActive=1)
              AND EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
            ORDER BY SortOrder,Name,{key};
            """, connection);
        command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@IncludeInactive", includeInactive)]);
        var result = new List<ProductVariantOption>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(ReadVariantOption(reader));
        return result;
    }

    public async Task<ProductVariantOption> SaveVariantOptionAsync(CatalogUserIdentity user,
        bool colors, Guid? id, SaveProductVariantOption request, DateTimeOffset now, CancellationToken ct)
    {
        var (table, key) = VariantTable(colors);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand($"""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                THROW 51020,'La sede no pertenece a la empresa.',1;
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF @Create=1
                INSERT {table}({key},TenantId,Code,Name,IsActive,SortOrder,CreatedAt)
                VALUES(@Id,@TenantId,@Code,@Name,@Active,@SortOrder,@Now);
            ELSE
            BEGIN
                UPDATE {table} SET Name=@Name,IsActive=@Active,SortOrder=@SortOrder,UpdatedAt=@Now
                WHERE TenantId=@TenantId AND {key}=@Id AND Code=@Code;
                IF @@ROWCOUNT=0 THROW 51020,'El registro no existe o se intentó cambiar su código.',1;
            END;
            DECLARE @Changes TABLE(BusinessId UNIQUEIDENTIFIER,CatalogChangeId BIGINT);
            INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
            OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @Changes
            SELECT business.BusinessId,product.ProductId,N'Upsert',@Now
            FROM dbo.Products product JOIN dbo.Businesses business ON business.TenantId=product.TenantId AND business.IsActive=1
            WHERE product.TenantId=@TenantId AND product.{key}=@Id;
            INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            SELECT NEWID(),BusinessId,N'Catalog',MAX(CatalogChangeId),@Now FROM @Changes GROUP BY BusinessId;
            COMMIT TRANSACTION;
            SELECT {key},Code,Name,IsActive,SortOrder FROM {table} WHERE TenantId=@TenantId AND {key}=@Id;
            """, connection);
        command.Parameters.AddRange([P("@TenantId", user.TenantId), P("@BusinessId", user.BusinessId), P("@Id", id ?? ids.NewId()),
            P("@Create", id is null), P("@Code", request.Code), P("@Name", request.Name), P("@Active", request.IsActive),
            P("@SortOrder", request.SortOrder), P("@Now", now)]);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new CatalogValidationException("No fue posible guardar el registro.");
            return ReadVariantOption(reader);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        { throw new CatalogConflictException("Ya existe un registro con ese código o nombre."); }
        catch (SqlException exception) when (exception.Number == 51020)
        { throw new CatalogValidationException(exception.Message); }
    }

    private static (string Table, string Key) VariantTable(bool colors) => colors
        ? ("catalog.ProductColors", "ProductColorId") : ("catalog.ProductSizes", "ProductSizeId");

    private static ProductVariantOption ReadVariantOption(SqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetInt32(4));
}
