using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IIntegrationConnectionRepository
{
    Task<IReadOnlyList<IntegrationConnection>> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default);
    Task<IntegrationConnection?> GetByBusinessProviderCapabilityAsync(
        Guid businessId,
        IntegrationProvider provider,
        IntegrationCapability capability,
        CancellationToken ct = default);
    Task<IntegrationConnection> CreateAsync(IntegrationConnection connection, CancellationToken ct = default);
    Task<IntegrationConnection> UpdateAsync(IntegrationConnection connection, CancellationToken ct = default);
}
