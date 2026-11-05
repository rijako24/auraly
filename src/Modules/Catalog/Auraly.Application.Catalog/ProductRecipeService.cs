using Auraly.Contracts.Catalog;
using Auraly.Domain.Catalog;

namespace Auraly.Application.Catalog;

public interface IProductRecipeStore
{
    Task<decimal> CompositionCostAsync(CatalogUserIdentity user, IReadOnlyCollection<ProductRecipeIngredientInput> components, CancellationToken ct);
    Task<ProductRecipeIngredientPage> IngredientOptionsAsync(CatalogUserIdentity user, string? search, int page, int pageSize, CancellationToken ct, string compositionKind = "Ingredients");
    Task<ProductRecipeDetail?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct);
    Task<ProductRecipeDetail> PublishAsync(CatalogUserIdentity user, Guid productId,
        PublishProductRecipeRequest request, DateTimeOffset publishedAt, CancellationToken ct);
}

public sealed class ProductRecipeService(IProductRecipeStore store, TimeProvider clock)
{
    public Task<decimal> CompositionCostAsync(CatalogUserIdentity user,
        IReadOnlyCollection<ProductRecipeIngredientInput> components, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.ReadCosts);
        ValidateComponents(components, allowEmpty: false);
        return store.CompositionCostAsync(user, components, ct);
    }
    public Task<ProductRecipeIngredientPage> IngredientOptionsAsync(CatalogUserIdentity user, string? search, int page, int pageSize, CancellationToken ct, string compositionKind = "Ingredients")
    {
        Require(user, CatalogPermissionCodes.Read);
        if (compositionKind is not ("Ingredients" or "Combo")) throw new CatalogValidationException("La modalidad no es válida.");
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 120)
            throw new CatalogValidationException("La búsqueda de ingredientes no es válida.");
        return store.IngredientOptionsAsync(user, search?.Trim(), page, pageSize, ct, compositionKind);
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
        if (request.CompositionKind is not ("Ingredients" or "Combo"))
            throw new CatalogValidationException("La modalidad de composición no es válida.");
        if (productId == Guid.Empty || request.ExpectedVersion < 0 || request.Ingredients is null ||
            (request.ExpectedVersion == 0 && request.Ingredients.Count == 0))
            throw new CatalogValidationException("Selecciona un producto y al menos un ingrediente.");
        ValidateComponents(request.Ingredients,productId);
        return store.PublishAsync(user, productId, request, clock.GetUtcNow(), ct);
    }

    internal static void ValidateComponents(IReadOnlyCollection<ProductRecipeIngredientInput> components,
        Guid? productId = null, bool allowEmpty = true)
    {
        if ((!allowEmpty && components.Count == 0) || components.Count > 100 || components.Any(line =>
            line.IngredientProductId == Guid.Empty || line.IngredientProductId == productId ||
            line.QuantityPerUnit <= 0 || line.QuantityPerUnit > 9999999999999.999999m ||
            decimal.Round(line.QuantityPerUnit,6) != line.QuantityPerUnit) ||
            components.Select(line => line.IngredientProductId).Distinct().Count() != components.Count)
            throw new CatalogValidationException("La composición contiene duplicados o cantidades inválidas (máximo seis decimales).");
    }

    private static void Require(CatalogUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new CatalogForbiddenException($"Permission '{permission}' is required.");
    }
}
