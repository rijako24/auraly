using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// IUnitOfWork en memoria. Solo implementa los repositorios que usa el orquestador.
/// El resto lanza NotImplementedException para detectar usos inesperados.
/// </summary>
public class InMemoryUnitOfWork : IUnitOfWork
{
    public IConversationRepository Conversations { get; }
    public IMessageRepository Messages { get; }
    public IConversationStateRepository ConversationStates { get; }
    public IServiceRepository Services { get; }
    public IServiceCategoryRepository ServiceCategories { get; }
    public IBusinessAttachmentRepository BusinessAttachments { get; }
    public IBusinessRepository Businesses { get; }
    public IBusinessWhatsAppNumberRepository BusinessWhatsAppNumbers => throw new NotImplementedException();
    public IBusinessConfigurationRepository BusinessConfigurations { get; }
    public ISystemConfigurationRepository SystemConfigurations { get; }
    public IConversationContextRepository ConversationContexts => throw new NotImplementedException();
    public IReservationRepository Reservations { get; }
    public IBusinessResourceRepository BusinessResources { get; }
    public IEmployeeRepository Employees { get; }
    public IEmployeeServiceRepository EmployeeServices { get; }
    public ILeadRepository Leads { get; }
    public IServiceAddOnRuleRepository ServiceAddOnRules { get; }
    public IReservationAddOnRepository ReservationAddOns { get; }

    public IPaymentTransactionRepository PaymentTransactions => throw new NotImplementedException();
    public IAppUserRepository AppUsers => throw new NotImplementedException();
    public IAppRoleRepository AppRoles => throw new NotImplementedException();
    public IPermissionRepository Permissions => throw new NotImplementedException();
    public IUserRoleRepository UserRoles => throw new NotImplementedException();
    public IRolePermissionRepository RolePermissions => throw new NotImplementedException();
    public IRefreshTokenRepository RefreshTokens => throw new NotImplementedException();
    public IUserExternalLoginRepository UserExternalLogins => throw new NotImplementedException();
    public IAuditLogRepository AuditLogs => throw new NotImplementedException();
    public ITenantRepository Tenants => throw new NotImplementedException();

    public IAgentRepository Agents => throw new NotImplementedException();
    public IAgentTypeRepository AgentTypes => throw new NotImplementedException();
    public IFlowDefinitionRepository FlowDefinitions => throw new NotImplementedException();
    public IFlowExecutionStateRepository FlowExecutionStates => throw new NotImplementedException();
    public IFlowNodeCatalogRepository FlowNodeCatalog => throw new NotImplementedException();
    public IKnowledgeSourceRepository KnowledgeSources => throw new NotImplementedException();

    private readonly Guid _businessId;

    public InMemoryUnitOfWork(Guid businessId)
    {
        _businessId = businessId;
        Conversations        = new InMemoryConversationRepository();
        Messages             = new InMemoryMessageRepository();
        ConversationStates   = new InMemoryConversationStateRepository();
        Services             = new InMemoryServiceRepository(businessId);
        ServiceCategories    = new InMemoryServiceCategoryRepository(businessId);
        BusinessAttachments  = new InMemoryBusinessAttachmentRepository();
        Businesses           = new InMemoryBusinessRepository(businessId);
        BusinessConfigurations = new InMemoryBusinessConfigurationRepository(businessId);
        SystemConfigurations = new InMemorySystemConfigurationRepository();
        Reservations         = new InMemoryReservationRepository();
        BusinessResources    = new InMemoryBusinessResourceRepository();
        Employees            = new InMemoryEmployeeRepository(businessId);
        EmployeeServices     = new InMemoryEmployeeServiceRepository();
        Leads                = new InMemoryLeadRepository();
        ServiceAddOnRules    = new InMemoryServiceAddOnRuleRepository(businessId);
        ReservationAddOns    = new InMemoryReservationAddOnRepository();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public void Dispose() { }
}
