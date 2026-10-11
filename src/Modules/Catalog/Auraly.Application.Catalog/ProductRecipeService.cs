using Auraly.Contracts.Catalog;
using Auraly.Domain.Catalog;

namespace Auraly.Application.Catalog;

public interface IProductRecipeStore
{
    Task<ProductRecipeIngredientPage> IngredientOptionsAsync(CatalogUserIdentity user, string? search, int page, int pageSize, CancellationToken ct);
    Task<ProductRecipeDetail?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct);
    Task<ProductRecipeDetail> PublishAsync(CatalogUserIdentity user, Guid productId,
        PublishProductRecipeRequest request, DateTimeOffset publishedAt, CancellationToken ct);
}

public sealed class ProductRecipeService(IProductRecipeStore store, TimeProvider clock)
{
    public Task<ProductRecipeIngredientPage> IngredientOptionsAsync(CatalogUserIdentity user, string? search, int page, int pageSize, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 120)
            throw new CatalogValidationException("La búsqueda de ingredientes no es válida.");
        return store.IngredientOptionsAsync(user, search?.Trim(), page, pageSize, ct);
    }
    public Task<ProductRecipeDetail?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        if (productId == Guid.Empty) throw new CatalogValidationException("El producto es obligatorio.");
        return store.GetAsync(user, productId, ct);
    }

    public Task<ProductRecipeDetail> PublishAsync(CatalogUserIdentity user, Guid productId,
        PublishProductRecipeRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        if (productId == Guid.Empty || request.ExpectedVersion < 0 || request.Ingredients is null ||
            (request.ExpectedVersion == 0 && request.Ingredients.Count == 0))
            throw new CatalogValidationException("Selecciona un producto y al menos un ingrediente.");
        if (request.Ingredients.Count > 100 || request.Ingredients.Any(line =>
                line.IngredientProductId == Guid.Empty || line.IngredientProductId == productId ||
                line.QuantityPerUnit <= 0 || line.QuantityPerUnit > 9999999999999.999999m) ||
            request.Ingredients.Select(line => line.IngredientProductId).Distinct().Count() != request.Ingredients.Count)
            throw new CatalogValidationException("La receta contiene ingredientes duplicados o cantidades inválidas.");
        return store.PublishAsync(user, productId, request, clock.GetUtcNow(), ct);
    }

    private static void Require(CatalogUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new CatalogForbiddenException($"Permission '{permission}' is required.");
    }
}
