using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Identity.DTOs;

public record ProductDto(
    Guid ProductId,
    Guid BusinessId,
    Guid? IntegrationConnectionId,
    string? ExternalProductId,
    ProductSource Source,
    string? Sku,
    string Name,
    string? Description,
    string? CategoryName,
    decimal UnitPrice,
    string Currency,
    bool ManageStock,
    decimal? StockQuantity,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastSyncedAt,
    string? ProductCode = null,
    string? AreaName = null);

public record UpdateProductStatusRequest(bool IsActive);
public record UpdateProductRequest(
    string Name,
    string? Description,
    string? CategoryName,
    decimal UnitPrice,
    string Currency);

public sealed record ProductCategoryAdminDto(
    Guid ProductCategoryId,
    Guid? ParentProductCategoryId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsBrowsable,
    int Depth,
    string Path);

public sealed record CreateProductCategoryRequest(
    Guid? ParentProductCategoryId,
    string Name,
    int DisplayOrder,
    bool IsBrowsable = true);

public sealed record UpdateProductCategoryRequest(
    Guid? ParentProductCategoryId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsBrowsable);