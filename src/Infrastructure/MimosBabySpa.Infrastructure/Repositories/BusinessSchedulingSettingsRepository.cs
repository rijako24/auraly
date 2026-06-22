using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class BusinessSchedulingSettingsRepository : IBusinessSchedulingSettingsRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessSchedulingSettingsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<BusinessSchedulingSettings?> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default) =>
        _context.BusinessSchedulingSettings
            .FirstOrDefaultAsync(s => s.BusinessId == businessId, ct);

    public Task<BusinessSchedulingSettings> AddAsync(BusinessSchedulingSettings settings, CancellationToken ct = default)
    {
        _context.BusinessSchedulingSettings.Add(settings);
        return Task.FromResult(settings);
    }

    public Task<BusinessSchedulingSettings> UpdateAsync(BusinessSchedulingSettings settings, CancellationToken ct = default)
    {
        _context.BusinessSchedulingSettings.Update(settings);
        return Task.FromResult(settings);
    }
}
