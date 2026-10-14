using System.Data;
using System.Text.Json;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlProductRecipeStore(
    SqlServerConnectionFactory connections, IAuralyIdGenerator ids) : IProductRecipeStore
{
    public async Task<ProductRecipeIngredientPage> IngredientOptionsAsync(
        CatalogUserIdentity user, string? search, int page, int pageSize, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT COUNT(*) FROM dbo.Products p
            WHERE p.TenantId=@TenantId AND p.IsActive=1 AND p.IsIngredient=1 AND p.ManageStock=1
              AND p.BaseUnitCode IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.ProductLinks link WHERE link.TenantId=p.TenantId
                  AND link.ChildProductId=p.ProductId AND link.IsActive=1 AND link.SharesInventory=1)
              AND (@Search IS NULL OR p.Name LIKE N'%'+@Search+N'%' OR p.ProductCode LIKE N'%'+@Search+N'%');
            SELECT p.ProductId,COALESCE(p.ProductCode,N''),p.Name,p.BaseUnitCode
            FROM dbo.Products p
            WHERE p.TenantId=@TenantId AND p.IsActive=1 AND p.IsIngredient=1 AND p.ManageStock=1
              AND p.BaseUnitCode IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.ProductLinks link WHERE link.TenantId=p.TenantId
                  AND link.ChildProductId=p.ProductId AND link.IsActive=1 AND link.SharesInventory=1)
              AND (@Search IS NULL OR p.Name LIKE N'%'+@Search+N'%' OR p.ProductCode LIKE N'%'+@Search+N'%')
            ORDER BY p.Name,p.ProductId OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search);
        command.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@Take", pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<ProductRecipeIngredientOption>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return new(items, page, pageSize, count, (count + pageSize - 1) / pageSize);
    }

    public async Task<ProductRecipeDetail?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await ReadAsync(connection, null, user.TenantId, productId, ct);
    }

    public async Task<ProductRecipeDetail> PublishAsync(CatalogUserIdentity user, Guid productId,
        PublishProductRecipeRequest request, DateTimeOffset publishedAt, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var graph = new SqlCommand("catalog.ProductRecipeGraphValidate", connection, transaction)
                   { CommandType = CommandType.StoredProcedure })
            {
                graph.Parameters.AddWithValue("@TenantId", user.TenantId);
                graph.Parameters.AddWithValue("@ProductId", productId);
                graph.Parameters.AddWithValue("@Ingredients", JsonSerializer.Serialize(request.Ingredients));
                await graph.ExecuteNonQueryAsync(ct);
            }
            const string sql = """
                IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                    THROW 51021, 'La sede no pertenece a este negocio.', 1;
                IF NOT EXISTS (SELECT 1 FROM dbo.Products WITH (UPDLOCK,HOLDLOCK)
                    WHERE TenantId=@TenantId AND ProductId=@ProductId AND IsActive=1 AND IsGenericProduct=0
                      AND (IsIngredient=0 OR ManageStock=1))
                    THROW 51021, 'El producto terminado no está activo o no admite receta.', 1;
                IF EXISTS (SELECT 1 FROM dbo.ProductLinks WHERE TenantId=@TenantId
                    AND ChildProductId=@ProductId AND IsActive=1 AND SharesInventory=1)
                    THROW 51024, 'El producto terminado debe manejar inventario propio para producirse.', 1;
                DECLARE @CurrentVersion int = ISNULL((SELECT MAX([Version]) FROM catalog.ProductRecipeVersions WITH (UPDLOCK,HOLDLOCK)
                    WHERE TenantId=@TenantId AND ProductId=@ProductId), 0);
                IF @CurrentVersion<>@ExpectedVersion
                    THROW 51024, 'La receta cambió. Actualiza antes de publicar.', 1;
                DECLARE @Items TABLE(LineNumber int PRIMARY KEY, IngredientProductId uniqueidentifier, QuantityPerUnit decimal(19,6));
                INSERT @Items(LineNumber,IngredientProductId,QuantityPerUnit)
                SELECT TRY_CONVERT(int,j.[key])+1, TRY_CONVERT(uniqueidentifier, JSON_VALUE(j.value,'$.IngredientProductId')),
                       TRY_CONVERT(decimal(19,6),JSON_VALUE(j.value,'$.QuantityPerUnit'))
                FROM OPENJSON(@Ingredients) j;
                IF (SELECT COUNT(*) FROM @Items)<>@ExpectedCount OR EXISTS (
                    SELECT 1 FROM @Items i LEFT JOIN dbo.Products p WITH (UPDLOCK,HOLDLOCK)
                        ON p.ProductId=i.IngredientProductId AND p.TenantId=@TenantId
                    WHERE p.ProductId IS NULL OR p.IsActive=0 OR p.IsIngredient=0 OR p.ManageStock=0
                       OR p.BaseUnitCode IS NULL OR i.QuantityPerUnit IS NULL OR i.QuantityPerUnit<=0
                       OR EXISTS (SELECT 1 FROM dbo.ProductLinks link WHERE link.TenantId=@TenantId
                           AND link.ChildProductId=p.ProductId AND link.IsActive=1 AND link.SharesInventory=1))
                    THROW 51024, 'Los ingredientes deben estar activos, marcados como ingredientes y manejar inventario.', 1;
                INSERT catalog.ProductRecipeVersions(ProductRecipeVersionId,TenantId,ProductId,[Version],IsEnabled,PublishedAt,PublishedByUserId)
                VALUES(@VersionId,@TenantId,@ProductId,@ExpectedVersion+1,CASE WHEN @ExpectedCount>0 THEN 1 ELSE 0 END,@PublishedAt,@UserId);
                INSERT catalog.ProductRecipeIngredients(ProductRecipeVersionId,LineNumber,TenantId,IngredientProductId,QuantityPerUnit,BaseUnitCodeSnapshot)
                SELECT @VersionId,i.LineNumber,@TenantId,i.IngredientProductId,i.QuantityPerUnit,p.BaseUnitCode
                FROM @Items i JOIN dbo.Products p ON p.TenantId=@TenantId AND p.ProductId=i.IngredientProductId;
                """;
            await using (var command = new SqlCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@ExpectedVersion", request.ExpectedVersion);
                command.Parameters.AddWithValue("@ExpectedCount", request.Ingredients.Count);
                command.Parameters.AddWithValue("@Ingredients", JsonSerializer.Serialize(request.Ingredients));
                command.Parameters.AddWithValue("@VersionId", ids.NewId());
                command.Parameters.AddWithValue("@PublishedAt", publishedAt);
                command.Parameters.AddWithValue("@UserId", user.UserId);
                await command.ExecuteNonQueryAsync(ct);
            }
            var result = await ReadAsync(connection, transaction, user.TenantId, productId, ct)
                ?? throw new InvalidOperationException("La receta publicada no se pudo leer.");
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException exception) when (exception.Number == 51021)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogValidationException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 51024 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogConflictException(exception.Message);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<ProductRecipeDetail?> ReadAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid tenantId, Guid productId, CancellationToken ct)
    {
        const string sql = """
            SELECT v.[Version],v.IsEnabled,v.PublishedAt,i.IngredientProductId,p.ProductCode,p.Name,
                   i.QuantityPerUnit,i.BaseUnitCodeSnapshot
            FROM catalog.ProductRecipeVersions v
            LEFT JOIN catalog.ProductRecipeIngredients i ON i.ProductRecipeVersionId=v.ProductRecipeVersionId
            LEFT JOIN dbo.Products p ON p.TenantId=v.TenantId AND p.ProductId=i.IngredientProductId
            WHERE v.TenantId=@TenantId AND v.ProductId=@ProductId
              AND v.[Version]=(SELECT MAX([Version]) FROM catalog.ProductRecipeVersions
                  WHERE TenantId=@TenantId AND ProductId=@ProductId)
            ORDER BY i.LineNumber;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@ProductId", productId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var ingredients = new List<ProductRecipeIngredientDetail>();
        int version = 0;
        bool isEnabled = false;
        DateTimeOffset publishedAt = default;
        while (await reader.ReadAsync(ct))
        {
            version = reader.GetInt32(0);
            isEnabled = reader.GetBoolean(1);
            publishedAt = reader.GetDateTimeOffset(2);
            if (!reader.IsDBNull(3))
                ingredients.Add(new(reader.GetGuid(3), reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.GetString(5), reader.GetDecimal(6), reader.GetString(7)));
        }
        return version == 0 ? null : new(productId, version, isEnabled, publishedAt, ingredients);
    }
}
