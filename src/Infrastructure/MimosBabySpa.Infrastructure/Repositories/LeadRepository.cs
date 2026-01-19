using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class LeadRepository : ILeadRepository
{
    private readonly ApplicationDbContext _context;

    public LeadRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Lead?> GetByUserNumberAsync(string userNumber)
    {
        return await _context.Leads
            .FirstOrDefaultAsync(l => l.UserNumber == userNumber);
    }

    public async Task<Lead?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber)
    {
        return await _context.Leads
            .FirstOrDefaultAsync(l => l.BusinessId == businessId && l.UserNumber == userNumber);
    }

    public Task<Lead> CreateAsync(Lead lead)
    {
        _context.Leads.Add(lead);
        return Task.FromResult(lead);
    }

    public Task<Lead> UpdateAsync(Lead lead)
    {
        _context.Leads.Update(lead);
        return Task.FromResult(lead);
    }

    public async Task<Lead?> GetByIdAsync(Guid leadId)
    {
        return await _context.Leads.FindAsync(leadId);
    }
}
