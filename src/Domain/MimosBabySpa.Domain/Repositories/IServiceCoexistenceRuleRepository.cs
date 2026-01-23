using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IServiceCoexistenceRuleRepository
{
    Task<ServiceCoexistenceRule?> GetByIdAsync(Guid ruleId);
    Task<IEnumerable<ServiceCoexistenceRule>> GetByBusinessIdAsync(Guid businessId);
    Task<ServiceCoexistenceRule?> GetByServicesAsync(Guid businessId, Guid serviceId1, Guid serviceId2);
    Task<ServiceCoexistenceRule> CreateAsync(ServiceCoexistenceRule rule);
    Task DeleteAsync(Guid ruleId);
}
