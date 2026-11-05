namespace Auraly.Contracts.Catalog;

public sealed record ProductRecipeIngredientInput(Guid IngredientProductId, decimal QuantityPerUnit);

public sealed record PublishProductRecipeRequest(int ExpectedVersion, IReadOnlyCollection<ProductRecipeIngredientInput> Ingredients,
    string CompositionKind = "Ingredients");

public sealed record ProductRecipeIngredientDetail(
    Guid IngredientProductId, string ProductCode, string Name,
    decimal QuantityPerUnit, string BaseUnitCode, decimal? UnitCostWithTax = null);

public sealed record ProductRecipeDetail(
    Guid ProductId, int Version, bool IsEnabled, DateTimeOffset PublishedAt,
    IReadOnlyCollection<ProductRecipeIngredientDetail> Ingredients, string CompositionKind = "Ingredients");

public sealed record ProductRecipeIngredientOption(
    Guid ProductId, string ProductCode, string Name, string BaseUnitCode, decimal? UnitCostWithTax = null);

public sealed record ProductRecipeIngredientPage(
    IReadOnlyList<ProductRecipeIngredientOption> Items, int Page, int PageSize, int TotalCount,
    int TotalPages);
