using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IServiceAddOnRuleRepository
{
    Task<IEnumerable<ServiceAddOnRule>> GetByBusinessIdAsync(Guid businessId);
}
