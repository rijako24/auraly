using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface ILeadRepository
{
    Task<Lead?> GetByUserNumberAsync(string userNumber);
    Task<Lead?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber);
    Task<Lead> CreateAsync(Lead lead);
    Task<Lead> UpdateAsync(Lead lead);
    Task<Lead?> GetByIdAsync(Guid leadId);
}
