using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IPromotionRepository
{
    Task<Promotion?> GetByIdAsync(Guid businessId, Guid promotionId, CancellationToken ct = default);
    Task<IReadOnlyList<Promotion>> GetActiveByBusinessIdAsync(Guid businessId, DateTime utcNow, CancellationToken ct = default);
    Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search,
        CancellationToken ct = default);
    Task<Promotion> CreateAsync(Promotion promotion, CancellationToken ct = default);
    Task<Promotion> UpdateAsync(Promotion promotion, CancellationToken ct = default);
}
