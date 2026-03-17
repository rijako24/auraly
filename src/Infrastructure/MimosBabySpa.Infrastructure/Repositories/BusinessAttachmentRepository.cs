using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
