using MimosBabySpa.Domain.Entities;
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
    public IConversationContextRepository ConversationContexts { get; }
    public ICustomerMemoryRepository CustomerMemory { get; }
    public IReservationRepository Reservations { get; }
    public IBusinessResourceRepository BusinessResources { get; }
    public IEmployeeRepository Employees { get; }
    public IEmployeeServiceRepository EmployeeServices { get; }
    public ILeadRepository Leads { get; }
    public IServiceAddOnRuleRepository ServiceAddOnRules { get; }
    public IReservationAddOnRepository ReservationAddOns { get; }
    public IPaymentTransactionRepository PaymentTransactions { get; }
    public IEnrollmentRepository Enrollments { get; }
    public IAppUserRepository AppUsers => throw new NotImplementedException();
    public IAppRoleRepository AppRoles => throw new NotImplementedException();
    public IPermissionRepository Permissions => throw new NotImplementedException();
    public IUserRoleRepository UserRoles => throw new NotImplementedException();
    public IRolePermissionRepository RolePermissions => throw new NotImplementedException();
    public IRefreshTokenRepository RefreshTokens => throw new NotImplementedException();
    public IUserExternalLoginRepository UserExternalLogins => throw new NotImplementedException();
    public IAuditLogRepository AuditLogs => throw new NotImplementedException();
    public ITenantRepository Tenants => throw new NotImplementedException();

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
        ConversationContexts   = new InMemoryConversationContextRepository();
        CustomerMemory         = new InMemoryCustomerMemoryRepository();
        Reservations         = new InMemoryReservationRepository();
        PaymentTransactions  = new InMemoryPaymentTransactionRepository();
        Enrollments          = new InMemoryEnrollmentRepository();
        BusinessResources    = new InMemoryBusinessResourceRepository();
        Employees            = new InMemoryEmployeeRepository(businessId);
        EmployeeServices     = new InMemoryEmployeeServiceRepository();
        Leads                = new InMemoryLeadRepository();
        ServiceAddOnRules    = new InMemoryServiceAddOnRuleRepository(businessId);
        ReservationAddOns    = new InMemoryReservationAddOnRepository();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
        action();

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default) =>
        await action();

    public void Dispose() { }
}
