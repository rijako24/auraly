using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class BusinessWhatsAppNumberRepository : IBusinessWhatsAppNumberRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessWhatsAppNumberRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BusinessWhatsAppNumber?> GetByWhatsAppPhoneNumberIdAsync(string whatsAppPhoneNumberId)
    {
        return await _context.BusinessWhatsAppNumbers
            .Include(n => n.Business)
            .FirstOrDefaultAsync(n => n.WhatsAppPhoneNumberId == whatsAppPhoneNumberId && n.IsActive);
    }

    public async Task<IEnumerable<BusinessWhatsAppNumber>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.BusinessWhatsAppNumbers
            .Where(n => n.BusinessId == businessId && n.IsActive)
            .ToListAsync();
    }

    public Task<BusinessWhatsAppNumber> CreateAsync(BusinessWhatsAppNumber whatsAppNumber)
    {
        _context.BusinessWhatsAppNumbers.Add(whatsAppNumber);
        return Task.FromResult(whatsAppNumber);
    }

    public Task<BusinessWhatsAppNumber> UpdateAsync(BusinessWhatsAppNumber whatsAppNumber)
    {
        _context.BusinessWhatsAppNumbers.Update(whatsAppNumber);
        return Task.FromResult(whatsAppNumber);
    }
}
