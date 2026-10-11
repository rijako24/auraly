using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public interface IRestaurantDigitalMenuStore
{
    Task<RestaurantDigitalMenuConfiguration?> GetConfigurationAsync(
        Guid tenantId, Guid businessId, CancellationToken ct);
    Task<RestaurantDigitalMenuConfiguration> SaveConfigurationAsync(
        Guid tenantId, Guid businessId, Guid userId,
        SaveRestaurantDigitalMenuRequest request, CancellationToken ct);
    Task<RestaurantDigitalMenuView?> GetPublicAsync(
        string publicCode, CancellationToken ct);
    Task<RestaurantDigitalMenuProductPage?> GetPublicProductsAsync(
        string publicCode, Guid categoryId, int page, CancellationToken ct);
    Task<RestaurantDigitalMenuView?> GetPreviewAsync(
        Guid tenantId, Guid businessId, CancellationToken ct);
    Task<RestaurantDigitalMenuProductPage?> GetPreviewProductsAsync(
        Guid tenantId, Guid businessId, Guid categoryId, int page,
        CancellationToken ct);
}

public sealed class RestaurantDigitalMenuService(IRestaurantDigitalMenuStore store)
{
    public Task<RestaurantDigitalMenuConfiguration?> GetConfigurationAsync(
        Guid tenantId, Guid businessId, IReadOnlySet<string> permissions,
        CancellationToken ct)
    {
        DemandConfiguration(tenantId, businessId, permissions);
        return store.GetConfigurationAsync(tenantId, businessId, ct);
    }

    public Task<RestaurantDigitalMenuConfiguration> SaveConfigurationAsync(
        Guid tenantId, Guid businessId, Guid userId,
        IReadOnlySet<string> permissions,
        SaveRestaurantDigitalMenuRequest request, CancellationToken ct)
    {
        DemandConfiguration(tenantId, businessId, permissions);
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 100 ||
            request.Introduction?.Trim().Length > 500)
            throw new RestaurantAccountValidationException(
                "El nombre o la descripción del menú no son válidos.");
        return store.SaveConfigurationAsync(tenantId, businessId, userId,
            request with { Title = request.Title.Trim(),
                Introduction = string.IsNullOrWhiteSpace(request.Introduction)
                    ? null : request.Introduction.Trim() }, ct);
    }

    public Task<RestaurantDigitalMenuView?> GetPublicAsync(
        string publicCode, CancellationToken ct) =>
        store.GetPublicAsync(ValidateCode(publicCode), ct);

    public Task<RestaurantDigitalMenuProductPage?> GetPublicProductsAsync(
        string publicCode, Guid categoryId, int page, CancellationToken ct)
    {
        if (categoryId == Guid.Empty || page is < 1 or > 1000)
            throw new RestaurantAccountValidationException(
                "La categoría o página no es válida.");
        return store.GetPublicProductsAsync(ValidateCode(publicCode),
            categoryId, page, ct);
    }

    public Task<RestaurantDigitalMenuView?> GetPreviewAsync(
        Guid tenantId, Guid businessId, IReadOnlySet<string> permissions,
        CancellationToken ct)
    {
        DemandConfiguration(tenantId, businessId, permissions);
        return store.GetPreviewAsync(tenantId, businessId, ct);
    }

    public Task<RestaurantDigitalMenuProductPage?> GetPreviewProductsAsync(
        Guid tenantId, Guid businessId, IReadOnlySet<string> permissions,
        Guid categoryId, int page, CancellationToken ct)
    {
        DemandConfiguration(tenantId, businessId, permissions);
        if (categoryId == Guid.Empty || page is < 1 or > 1000)
            throw new RestaurantAccountValidationException(
                "La categoría o página no es válida.");
        return store.GetPreviewProductsAsync(tenantId, businessId,
            categoryId, page, ct);
    }

    private static string ValidateCode(string? code)
    {
        if (code is null || code.Length != 32 ||
            !code.All(character => char.IsAsciiHexDigit(character)))
            throw new RestaurantAccountValidationException(
                "El enlace del menú no es válido.");
        return code.ToLowerInvariant();
    }

    private static void DemandConfiguration(Guid tenantId, Guid businessId,
        IReadOnlySet<string> permissions)
    {
        if (tenantId == Guid.Empty || businessId == Guid.Empty ||
            !permissions.Contains("businesses.update"))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para configurar el menú digital.");
    }
}
