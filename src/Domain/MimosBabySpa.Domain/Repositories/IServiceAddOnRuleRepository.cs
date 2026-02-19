using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IServiceAddOnRuleRepository
{
    Task<IEnumerable<ServiceAddOnRule>> GetByBusinessIdAsync(Guid businessId);
}
