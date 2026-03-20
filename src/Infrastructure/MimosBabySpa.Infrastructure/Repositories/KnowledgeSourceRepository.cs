using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class KnowledgeSourceRepository : IKnowledgeSourceRepository
{
    private readonly ApplicationDbContext _db;

    public KnowledgeSourceRepository(ApplicationDbContext db) => _db = db;

    public async Task<KnowledgeSource?> GetByIdAsync(
        Guid knowledgeSourceId, CancellationToken ct = default) =>
        await _db.KnowledgeSources.FindAsync([knowledgeSourceId], ct);

    public async Task<IReadOnlyList<KnowledgeSource>> GetByBusinessAsync(
        Guid businessId, CancellationToken ct = default) =>
        await _db.KnowledgeSources
            .Where(ks => ks.BusinessId == businessId && ks.IsActive)
            .OrderBy(ks => ks.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<KnowledgeSource>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return await _db.KnowledgeSources
            .Where(ks => idList.Contains(ks.KnowledgeSourceId) && ks.IsActive)
            .ToListAsync(ct);
    }

    public Task<KnowledgeSource> AddAsync(
        KnowledgeSource source, CancellationToken ct = default)
    {
        if (source.KnowledgeSourceId == Guid.Empty)
            source.KnowledgeSourceId = Guid.NewGuid();
        source.CreatedAt = DateTime.UtcNow;
        _db.KnowledgeSources.Add(source);
        return Task.FromResult(source);
    }

    public Task UpdateAsync(KnowledgeSource source, CancellationToken ct = default)
    {
        source.UpdatedAt = DateTime.UtcNow;
        _db.KnowledgeSources.Update(source);
        return Task.CompletedTask;
    }
}
