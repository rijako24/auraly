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
    public async Task<decimal> CompositionCostAsync(CatalogUserIdentity user,
        IReadOnlyCollection<ProductRecipeIngredientInput> components, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT Cost,Missing FROM catalog.ProductCompositionCost(@TenantId,@BusinessId,@Components)
            WHERE EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId);
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.Add("@Components", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(components);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0) || reader.GetDecimal(0) <= 0 || !reader.IsDBNull(1) && reader.GetInt32(1) > 0)
            throw new CatalogValidationException("Falta costo o impuesto de compra de un componente.");
        return reader.GetDecimal(0);
    }
    public async Task<ProductRecipeIngredientPage> IngredientOptionsAsync(
        CatalogUserIdentity user, string? search, int page, int pageSize, CancellationToken ct, string compositionKind = "Ingredients")
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT COUNT(*) FROM dbo.Products p
            WHERE p.TenantId=@TenantId AND p.IsActive=1 AND p.IsIngredient=@IsIngredient AND p.ManageStock=1 AND (@IsIngredient=1 OR p.IsCombo=0)
              AND p.BaseUnitCode IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.ProductLinks link WHERE link.TenantId=p.TenantId
                  AND link.ChildProductId=p.ProductId AND link.IsActive=1 AND link.SharesInventory=1)
              AND (@Search IS NULL OR p.Name LIKE N'%'+@Search+N'%' OR p.ProductCode LIKE N'%'+@Search+N'%');
            WITH SelectedPage AS (
            SELECT p.ProductId,COALESCE(p.ProductCode,N'') ProductCode,p.Name,p.BaseUnitCode
            FROM dbo.Products p
            WHERE p.TenantId=@TenantId AND p.IsActive=1 AND p.IsIngredient=@IsIngredient AND p.ManageStock=1 AND (@IsIngredient=1 OR p.IsCombo=0)
              AND p.BaseUnitCode IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.ProductLinks link WHERE link.TenantId=p.TenantId
                  AND link.ChildProductId=p.ProductId AND link.IsActive=1 AND link.SharesInventory=1)
              AND (@Search IS NULL OR p.Name LIKE N'%'+@Search+N'%' OR p.ProductCode LIKE N'%'+@Search+N'%')
            ORDER BY p.Name,p.ProductId OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY)
            SELECT p.ProductId,p.ProductCode,p.Name,p.BaseUnitCode,cost.UnitCostWithTax
            FROM SelectedPage p
            OUTER APPLY(SELECT UnitCostWithTax FROM dbo.ProductPriceCostBasis(@TenantId,@BusinessId,p.ProductId)
              WHERE @CanReadCosts=1) cost
            ORDER BY p.Name,p.ProductId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@CanReadCosts", user.Permissions.Contains(CatalogPermissionCodes.ReadCosts));
        command.Parameters.AddWithValue("@IsIngredient", compositionKind == "Ingredients");
        command.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search);
        command.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@Take", pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<ProductRecipeIngredientOption>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4)));
        return new(items, page, pageSize, count, (count + pageSize - 1) / pageSize);
    }

    public async Task<ProductRecipeDetail?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await ReadAsync(connection, null, user, productId, ct);
    }

    public async Task<ProductRecipeDetail> PublishAsync(CatalogUserIdentity user, Guid productId,
        PublishProductRecipeRequest request, DateTimeOffset publishedAt, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var command = new SqlCommand("catalog.ProductCompositionSave", connection, transaction)
                   { CommandType = CommandType.StoredProcedure })
            {
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@ExpectedVersion", request.ExpectedVersion);
                command.Parameters.AddWithValue("@Ingredients", JsonSerializer.Serialize(request.Ingredients));
                command.Parameters.AddWithValue("@VersionId", ids.NewId());
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@Now", publishedAt);
                command.Parameters.AddWithValue("@CompositionKind", request.CompositionKind);
                await command.ExecuteNonQueryAsync(ct);
            }
            var result = await ReadAsync(connection, transaction, user, productId, ct)
                ?? throw new InvalidOperationException("La receta publicada no se pudo leer.");
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException exception) when (exception.Number is 51020 or 51021)
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
        SqlConnection connection, SqlTransaction? transaction, CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        const string sql = """
            SELECT v.[Version],v.IsEnabled,v.PublishedAt,i.IngredientProductId,p.ProductCode,p.Name,
                   i.QuantityPerUnit,i.BaseUnitCodeSnapshot,v.CompositionKind,cost.UnitCostWithTax
            FROM catalog.ProductCompositionVersions v
            LEFT JOIN catalog.ProductCompositionComponents i ON i.ProductRecipeVersionId=v.ProductRecipeVersionId
            LEFT JOIN dbo.Products p ON p.TenantId=v.TenantId AND p.ProductId=i.IngredientProductId
            OUTER APPLY(SELECT UnitCostWithTax FROM dbo.ProductPriceCostBasis(@TenantId,@BusinessId,p.ProductId)
              WHERE @CanReadCosts=1) cost
            WHERE v.TenantId=@TenantId AND v.ProductId=@ProductId
              AND v.[Version]=(SELECT MAX([Version]) FROM catalog.ProductCompositionVersions
                  WHERE TenantId=@TenantId AND ProductId=@ProductId)
            ORDER BY i.LineNumber;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@CanReadCosts", user.Permissions.Contains(CatalogPermissionCodes.ReadCosts));
        command.Parameters.AddWithValue("@ProductId", productId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var ingredients = new List<ProductRecipeIngredientDetail>();
        int version = 0;
        bool isEnabled = false;
        string kind = "Ingredients";
        DateTimeOffset publishedAt = default;
        while (await reader.ReadAsync(ct))
        {
            version = reader.GetInt32(0);
            isEnabled = reader.GetBoolean(1);
            kind = reader.GetString(8);
            publishedAt = reader.GetDateTimeOffset(2);
            if (!reader.IsDBNull(3))
                ingredients.Add(new(reader.GetGuid(3), reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.GetString(5), reader.GetDecimal(6), reader.GetString(7),
                    reader.IsDBNull(9) ? null : reader.GetDecimal(9)));
        }
        return version == 0 ? null : new(productId, version, isEnabled, publishedAt, ingredients, kind);
    }
}
