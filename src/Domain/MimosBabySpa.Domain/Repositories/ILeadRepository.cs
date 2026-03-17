using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface ILeadRepository
{
    Task<Lead?> GetByIdAsync(Guid leadId);
    Task<Lead?> GetByUserNumberAsync(string userNumber);
    Task<Lead?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber);
    Task<IEnumerable<Lead>> GetByBusinessIdAsync(Guid businessId);
    Task<(IReadOnlyList<Lead> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<Lead> CreateAsync(Lead lead);
    Task<Lead> UpdateAsync(Lead lead);
}
