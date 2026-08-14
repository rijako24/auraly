using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IBusinessInboundContactRepository
{
    Task<BusinessInboundContact?> GetByIdAsync(Guid contactId, CancellationToken ct = default);
    Task<BusinessInboundContact?> GetByPhoneAsync(Guid businessId, string normalizedPhone, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessInboundContact>> GetByBusinessAsync(Guid businessId, bool includeInactive = false, CancellationToken ct = default);
Task<BusinessInboundContact?> GetActiveByPhoneAsync(Guid businessId, string phone, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessInboundContact>> GetActiveByBusinessAsync(Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessInboundContact>> GetActiveByBusinessAndTypeAsync(Guid businessId, string type, CancellationToken ct = default);
    Task<BusinessInboundContact> AddAsync(BusinessInboundContact contact, CancellationToken ct = default);
    Task UpdateAsync(BusinessInboundContact contact, CancellationToken ct = default);
}
