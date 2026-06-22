using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IBusinessSchedulingSettingsRepository
{
    Task<BusinessSchedulingSettings?> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default);
    Task<BusinessSchedulingSettings> AddAsync(BusinessSchedulingSettings settings, CancellationToken ct = default);
    Task<BusinessSchedulingSettings> UpdateAsync(BusinessSchedulingSettings settings, CancellationToken ct = default);
}
