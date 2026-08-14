using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class BusinessAttachmentRepository : IBusinessAttachmentRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessAttachmentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BusinessAttachment?> GetByIdAsync(Guid businessAttachmentId)
    {
        return await _context.BusinessAttachments
            .FirstOrDefaultAsync(a => a.BusinessAttachmentId == businessAttachmentId && a.IsActive);
    }

    public async Task<IEnumerable<BusinessAttachment>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.BusinessAttachments
            .Where(a => a.BusinessId == businessId && a.IsActive)
            .OrderBy(a => a.Description ?? a.Filename ?? a.BlobPath)
            .ToListAsync();
    }
}
