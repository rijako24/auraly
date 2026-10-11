namespace Auraly.Contracts.Catalog;

public sealed record ProductRecipeIngredientInput(Guid IngredientProductId, decimal QuantityPerUnit);

public sealed record PublishProductRecipeRequest(int ExpectedVersion, IReadOnlyCollection<ProductRecipeIngredientInput> Ingredients);

public sealed record ProductRecipeIngredientDetail(
    Guid IngredientProductId, string ProductCode, string Name,
    decimal QuantityPerUnit, string BaseUnitCode);

public sealed record ProductRecipeDetail(
    Guid ProductId, int Version, bool IsEnabled, DateTimeOffset PublishedAt,
    IReadOnlyCollection<ProductRecipeIngredientDetail> Ingredients);

public sealed record ProductRecipeIngredientOption(
    Guid ProductId, string ProductCode, string Name, string BaseUnitCode);

public sealed record ProductRecipeIngredientPage(
    IReadOnlyList<ProductRecipeIngredientOption> Items, int Page, int PageSize, int TotalCount,
    int TotalPages);
