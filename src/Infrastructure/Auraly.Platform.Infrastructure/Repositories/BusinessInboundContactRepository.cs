using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class BusinessInboundContactRepository : IBusinessInboundContactRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessInboundContactRepository(ApplicationDbContext context) => _context = context;

    public Task<BusinessInboundContact?> GetByIdAsync(Guid contactId, CancellationToken ct = default) =>
        _context.BusinessInboundContacts
            .Include(c => c.InboundAgent)
            .FirstOrDefaultAsync(c => c.BusinessInboundContactId == contactId, ct);

    public Task<BusinessInboundContact?> GetByPhoneAsync(Guid businessId, string normalizedPhone, CancellationToken ct = default) =>
        _context.BusinessInboundContacts
            .Include(c => c.InboundAgent)
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.PhoneNormalized == normalizedPhone, ct);

    public Task<BusinessInboundContact?> GetActiveByPhoneAsync(Guid businessId, string phone, CancellationToken ct = default)
    {
        var normalized = NormalizePhone(phone);
        return _context.BusinessInboundContacts
            .Include(c => c.InboundAgent)
            .FirstOrDefaultAsync(c => c.BusinessId == businessId
                && c.PhoneNormalized == normalized
                && c.IsActive
                && c.InboundAgent.BusinessId == businessId
                && c.InboundAgent.IsActive, ct);
    }

    public async Task<IReadOnlyList<BusinessInboundContact>> GetByBusinessAsync(Guid businessId, bool includeInactive = false, CancellationToken ct = default) =>
        await _context.BusinessInboundContacts
            .Include(c => c.InboundAgent)
            .Where(c => c.BusinessId == businessId && (includeInactive || c.IsActive))
            .OrderBy(c => c.Type)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BusinessInboundContact>> GetActiveByBusinessAsync(Guid businessId, CancellationToken ct = default) =>
        await _context.BusinessInboundContacts
            .Include(c => c.InboundAgent)
            .Where(c => c.BusinessId == businessId
                && c.IsActive
                && c.InboundAgent.BusinessId == businessId
                && c.InboundAgent.IsActive)
            .OrderBy(c => c.Type)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BusinessInboundContact>> GetActiveByBusinessAndTypeAsync(Guid businessId, string type, CancellationToken ct = default) =>
        await _context.BusinessInboundContacts
            .Include(c => c.InboundAgent)
            .Where(c => c.BusinessId == businessId
                && c.Type == type
                && c.IsActive
                && c.InboundAgent.BusinessId == businessId
                && c.InboundAgent.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public Task<BusinessInboundContact> AddAsync(BusinessInboundContact contact, CancellationToken ct = default)
    {
        _context.BusinessInboundContacts.Add(contact);
        return Task.FromResult(contact);
    }

    public Task UpdateAsync(BusinessInboundContact contact, CancellationToken ct = default)
    {
        _context.BusinessInboundContacts.Update(contact);
        return Task.CompletedTask;
    }

    private static string NormalizePhone(string phone) => new(phone.Where(char.IsDigit).ToArray());
}