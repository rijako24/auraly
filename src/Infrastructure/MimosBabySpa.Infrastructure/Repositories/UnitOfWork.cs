using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IConversationRepository? _conversations;
    private IMessageRepository? _messages;
    private ILeadRepository? _leads;
    private IBusinessRepository? _businesses;
    private IBusinessWhatsAppNumberRepository? _businessWhatsAppNumbers;
    private IBusinessConfigurationRepository? _businessConfigurations;
    private ISystemConfigurationRepository? _systemConfigurations;
    private IConversationContextRepository? _conversationContexts;
    private ICustomerMemoryRepository? _customerMemory;
    private IReservationRepository? _reservations;
    private IServiceRepository? _services;
    private IServiceCategoryRepository? _serviceCategories;
    private IBusinessAttachmentRepository? _businessAttachments;
    private IBusinessResourceRepository? _businessResources;
    private IEmployeeRepository? _employees;
    private IEmployeeServiceRepository? _employeeServices;
    private IConversationStateRepository? _conversationStates;
    private IServiceAddOnRuleRepository? _serviceAddOnRules;
    private IReservationAddOnRepository? _reservationAddOns;
    private IPaymentTransactionRepository? _paymentTransactions;
    private IEnrollmentRepository? _enrollments;
    private IAppUserRepository? _appUsers;
    private IAppRoleRepository? _appRoles;
    private IPermissionRepository? _permissions;
    private IUserRoleRepository? _userRoles;
    private IRolePermissionRepository? _rolePermissions;
    private IRefreshTokenRepository? _refreshTokens;
    private IUserExternalLoginRepository? _userExternalLogins;
    private IAuditLogRepository? _auditLogs;
    private ITenantRepository? _tenants;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IConversationRepository Conversations =>
        _conversations ??= new ConversationRepository(_context);

    public IMessageRepository Messages =>
        _messages ??= new MessageRepository(_context);

    public ILeadRepository Leads =>
        _leads ??= new LeadRepository(_context);

    public IBusinessRepository Businesses =>
        _businesses ??= new BusinessRepository(_context);

    public IBusinessWhatsAppNumberRepository BusinessWhatsAppNumbers =>
        _businessWhatsAppNumbers ??= new BusinessWhatsAppNumberRepository(_context);

    public IBusinessConfigurationRepository BusinessConfigurations =>
        _businessConfigurations ??= new BusinessConfigurationRepository(_context);

    public ISystemConfigurationRepository SystemConfigurations =>
        _systemConfigurations ??= new SystemConfigurationRepository(_context);

    public IConversationContextRepository ConversationContexts =>
        _conversationContexts ??= new ConversationContextRepository(_context);

    public ICustomerMemoryRepository CustomerMemory =>
        _customerMemory ??= new CustomerMemoryRepository(_context);

    public IReservationRepository Reservations =>
        _reservations ??= new ReservationRepository(_context);

    public IServiceRepository Services =>
        _services ??= new ServiceRepository(_context);

    public IServiceCategoryRepository ServiceCategories =>
        _serviceCategories ??= new ServiceCategoryRepository(_context);

    public IBusinessAttachmentRepository BusinessAttachments =>
        _businessAttachments ??= new BusinessAttachmentRepository(_context);

    public IBusinessResourceRepository BusinessResources =>
        _businessResources ??= new BusinessResourceRepository(_context);

    public IEmployeeRepository Employees =>
        _employees ??= new EmployeeRepository(_context);

    public IEmployeeServiceRepository EmployeeServices =>
        _employeeServices ??= new EmployeeServiceRepository(_context);

    public IConversationStateRepository ConversationStates =>
        _conversationStates ??= new ConversationStateRepository(_context);

    public IServiceAddOnRuleRepository ServiceAddOnRules =>
        _serviceAddOnRules ??= new ServiceAddOnRuleRepository(_context);

    public IReservationAddOnRepository ReservationAddOns =>
        _reservationAddOns ??= new ReservationAddOnRepository(_context);

    public IPaymentTransactionRepository PaymentTransactions =>
        _paymentTransactions ??= new PaymentTransactionRepository(_context);

    public IEnrollmentRepository Enrollments =>
        _enrollments ??= new EnrollmentRepository(_context);

    public IAppUserRepository AppUsers =>
        _appUsers ??= new AppUserRepository(_context);

    public IAppRoleRepository AppRoles =>
        _appRoles ??= new AppRoleRepository(_context);

    public IPermissionRepository Permissions =>
        _permissions ??= new PermissionRepository(_context);

    public IUserRoleRepository UserRoles =>
        _userRoles ??= new UserRoleRepository(_context);

    public IRolePermissionRepository RolePermissions =>
        _rolePermissions ??= new RolePermissionRepository(_context);

    public IRefreshTokenRepository RefreshTokens =>
        _refreshTokens ??= new RefreshTokenRepository(_context);

    public IUserExternalLoginRepository UserExternalLogins =>
        _userExternalLogins ??= new UserExternalLoginRepository(_context);

    public IAuditLogRepository AuditLogs =>
        _auditLogs ??= new AuditLogRepository(_context);

    public ITenantRepository Tenants =>
        _tenants ??= new TenantRepository(_context);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await action();
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
