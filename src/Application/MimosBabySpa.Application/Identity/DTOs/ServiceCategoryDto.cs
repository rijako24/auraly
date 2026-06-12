namespace MimosBabySpa.Application.Identity.DTOs;

public record ServiceCategoryDto(
    Guid ServiceCategoryId,
    Guid BusinessId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTime CreatedAt);
