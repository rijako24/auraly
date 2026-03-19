using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IKnowledgeSourceRepository
{
    Task<KnowledgeSource?> GetByIdAsync(Guid knowledgeSourceId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeSource>> GetByBusinessAsync(Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeSource>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<KnowledgeSource> AddAsync(KnowledgeSource source, CancellationToken ct = default);
    Task UpdateAsync(KnowledgeSource source, CancellationToken ct = default);
}
