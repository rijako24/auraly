using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.DTOs;

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
    DateTime? LastSyncedAt);

public record UpdateProductStatusRequest(bool IsActive);