using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class BusinessRepository : IBusinessRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Business?> GetByIdAsync(Guid businessId)
    {
        return await _context.Businesses
            .FirstOrDefaultAsync(b => b.BusinessId == businessId);
    }

    public async Task<Business?> GetByIdWithConfigurationAsync(Guid businessId)
    {
        return await _context.Businesses
            .Include(b => b.Configurations)
            .Include(b => b.WhatsAppNumbers)
            .FirstOrDefaultAsync(b => b.BusinessId == businessId);
    }

    public Task<Business> CreateAsync(Business business)
    {
        _context.Businesses.Add(business);
        return Task.FromResult(business);
    }

    public Task<Business> UpdateAsync(Business business)
    {
        _context.Businesses.Update(business);
        return Task.FromResult(business);
    }
}
