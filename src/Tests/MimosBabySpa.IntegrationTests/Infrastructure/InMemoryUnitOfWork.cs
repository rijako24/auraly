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
    public IBusinessWorkingHourRepository BusinessWorkingHours { get; }
    public IEmployeeWorkingHourRepository EmployeeWorkingHours { get; }
    public IEmployeeScheduleExceptionRepository EmployeeScheduleExceptions { get; }
    public IIntegrationConnectionRepository IntegrationConnections { get; }
    public IReservationIntegrationEventRepository ReservationIntegrationEvents { get; }
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
    public ISubscriptionPlanRepository SubscriptionPlans { get; }
    public IBusinessSubscriptionRepository BusinessSubscriptions { get; }
    public IBusinessUsagePeriodRepository BusinessUsagePeriods { get; }
    public IUsageLedgerRepository UsageLedger { get; }
    public IUsageCostRateRepository UsageCostRates { get; }

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
        BusinessWorkingHours = new InMemoryBusinessWorkingHourRepository(businessId);
        EmployeeWorkingHours = new InMemoryEmployeeWorkingHourRepository();
        EmployeeScheduleExceptions = new InMemoryEmployeeScheduleExceptionRepository();
        IntegrationConnections = new InMemoryIntegrationConnectionRepository();
        ReservationIntegrationEvents = new InMemoryReservationIntegrationEventRepository();
        Leads                = new InMemoryLeadRepository();
        ServiceAddOnRules    = new InMemoryServiceAddOnRuleRepository(businessId);
        ReservationAddOns    = new InMemoryReservationAddOnRepository();
        SubscriptionPlans    = new InMemorySubscriptionPlanRepository();
        BusinessSubscriptions = new InMemoryBusinessSubscriptionRepository(businessId);
        BusinessUsagePeriods = new InMemoryBusinessUsagePeriodRepository(businessId);
        UsageLedger          = new InMemoryUsageLedgerRepository();
        UsageCostRates       = new InMemoryUsageCostRateRepository();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
        action();

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default) =>
        await action();

    public void Dispose() { }
}

internal sealed class InMemorySubscriptionPlanRepository : ISubscriptionPlanRepository
{
    private readonly SubscriptionPlan _plan = new()
    {
        SubscriptionPlanId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Code = "essential",
        Name = "Esencial",
        MonthlyPriceCop = 389999,
        IncludedCredits = 15000,
        MaxVariableCostCop = 100000,
        MaxVariableCostPercent = 25.64m,
        IncludedAgents = 1,
        IncludedUsers = 1,
        IncludedWorkspaces = 1
    };

    public Task<IReadOnlyList<SubscriptionPlan>> GetActiveAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>([_plan]);

    public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult<SubscriptionPlan?>(_plan.Code == code ? _plan : null);
}

internal sealed class InMemoryBusinessSubscriptionRepository : IBusinessSubscriptionRepository
{
    private readonly BusinessSubscription _subscription;

    public InMemoryBusinessSubscriptionRepository(Guid businessId)
    {
        _subscription = new BusinessSubscription
        {
            BusinessSubscriptionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BusinessId = businessId,
            SubscriptionPlanId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CurrentPeriodStart = DateTime.UtcNow.Date.AddDays(-1),
            CurrentPeriodEnd = DateTime.UtcNow.Date.AddMonths(1),
            PlanCodeSnapshot = "essential",
            PlanNameSnapshot = "Esencial",
            MonthlyPriceCop = 389999,
            IncludedCredits = 15000,
            MaxVariableCostCop = 100000,
            MaxVariableCostPercent = 25.64m
        };
    }

    public Task<BusinessSubscription?> GetActiveByBusinessIdAsync(Guid businessId, CancellationToken ct = default) =>
        Task.FromResult<BusinessSubscription?>(_subscription.BusinessId == businessId ? _subscription : null);

    public Task<BusinessSubscription> AddAsync(BusinessSubscription subscription, CancellationToken ct = default) =>
        Task.FromResult(subscription);

    public Task UpdateAsync(BusinessSubscription subscription, CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class InMemoryBusinessUsagePeriodRepository : IBusinessUsagePeriodRepository
{
    private readonly BusinessUsagePeriod _period;

    public InMemoryBusinessUsagePeriodRepository(Guid businessId)
    {
        var subscription = new BusinessSubscription
        {
            BusinessSubscriptionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BusinessId = businessId,
            PlanCodeSnapshot = "essential",
            PlanNameSnapshot = "Esencial",
            CurrentPeriodStart = DateTime.UtcNow.Date.AddDays(-1),
            CurrentPeriodEnd = DateTime.UtcNow.Date.AddMonths(1),
            IncludedCredits = 15000,
            MaxVariableCostCop = 100000
        };

        _period = new BusinessUsagePeriod
        {
            BusinessUsagePeriodId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            BusinessSubscriptionId = subscription.BusinessSubscriptionId,
            BusinessSubscription = subscription,
            BusinessId = businessId,
            PeriodStart = subscription.CurrentPeriodStart,
            PeriodEnd = subscription.CurrentPeriodEnd,
            CreditsIncluded = 15000,
            VariableCostLimitCop = 100000
        };
    }

    public Task<BusinessUsagePeriod?> GetCurrentAsync(Guid businessSubscriptionId, DateTime utcNow, CancellationToken ct = default) =>
        Task.FromResult<BusinessUsagePeriod?>(_period.BusinessSubscriptionId == businessSubscriptionId ? _period : null);

    public Task<BusinessUsagePeriod?> GetCurrentByBusinessIdAsync(Guid businessId, DateTime utcNow, CancellationToken ct = default) =>
        Task.FromResult<BusinessUsagePeriod?>(_period.BusinessId == businessId ? _period : null);

    public Task<BusinessUsagePeriod> AddAsync(BusinessUsagePeriod period, CancellationToken ct = default) =>
        Task.FromResult(period);

    public Task UpdateAsync(BusinessUsagePeriod period, CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class InMemoryUsageLedgerRepository : IUsageLedgerRepository
{
    private readonly List<UsageLedgerEntry> _entries = [];

    public Task<UsageLedgerEntry> AddAsync(UsageLedgerEntry entry, CancellationToken ct = default)
    {
        _entries.Add(entry);
        return Task.FromResult(entry);
    }

    public Task<IReadOnlyList<UsageLedgerEntry>> GetRecentByBusinessIdAsync(Guid businessId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UsageLedgerEntry>>(
            _entries.Where(e => e.BusinessId == businessId).Take(limit).ToList());
}

internal sealed class InMemoryUsageCostRateRepository : IUsageCostRateRepository
{
    public Task<UsageCostRate?> GetActiveAsync(string code, Domain.Enums.UsageOperationType operationType, DateTime utcNow, CancellationToken ct = default) =>
        Task.FromResult<UsageCostRate?>(null);

    public Task<IReadOnlyList<UsageCostRate>> GetActiveAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UsageCostRate>>([]);
}

internal sealed class InMemoryBusinessWorkingHourRepository : IBusinessWorkingHourRepository
{
    private readonly List<BusinessWorkingHour> _hours;

    public InMemoryBusinessWorkingHourRepository(Guid businessId)
    {
        _hours =
        [
            New(businessId, DayOfWeek.Monday, "08:00", "18:00"),
            New(businessId, DayOfWeek.Tuesday, "08:00", "18:00"),
            New(businessId, DayOfWeek.Wednesday, "08:00", "18:00"),
            New(businessId, DayOfWeek.Thursday, "08:00", "18:00"),
            New(businessId, DayOfWeek.Friday, "08:00", "18:00"),
            New(businessId, DayOfWeek.Saturday, "08:00", "13:00")
        ];
    }

    public Task<IReadOnlyList<BusinessWorkingHour>> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BusinessWorkingHour>>(_hours.Where(h => h.BusinessId == businessId).ToList());

    public Task ReplaceForBusinessAsync(Guid businessId, IEnumerable<BusinessWorkingHour> workingHours, CancellationToken ct = default)
    {
        _hours.RemoveAll(h => h.BusinessId == businessId);
        _hours.AddRange(workingHours);
        return Task.CompletedTask;
    }

    private static BusinessWorkingHour New(Guid businessId, DayOfWeek day, string open, string close) => new()
    {
        BusinessWorkingHourId = Guid.NewGuid(),
        BusinessId = businessId,
        DayOfWeek = day,
        OpenTime = TimeSpan.Parse(open),
        CloseTime = TimeSpan.Parse(close),
        IsActive = true
    };
}

internal sealed class InMemoryEmployeeWorkingHourRepository : IEmployeeWorkingHourRepository
{
    private readonly List<EmployeeWorkingHour> _hours = [];

    public Task<IReadOnlyList<EmployeeWorkingHour>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmployeeWorkingHour>>(_hours.Where(h => h.EmployeeId == employeeId).ToList());

    public Task<IReadOnlyList<EmployeeWorkingHour>> GetByEmployeeIdsAsync(IEnumerable<Guid> employeeIds, CancellationToken ct = default)
    {
        var ids = employeeIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<EmployeeWorkingHour>>(_hours.Where(h => ids.Contains(h.EmployeeId)).ToList());
    }

    public Task ReplaceForEmployeeAsync(Guid employeeId, IEnumerable<EmployeeWorkingHour> workingHours, CancellationToken ct = default)
    {
        _hours.RemoveAll(h => h.EmployeeId == employeeId);
        _hours.AddRange(workingHours);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryEmployeeScheduleExceptionRepository : IEmployeeScheduleExceptionRepository
{
    private readonly List<EmployeeScheduleException> _exceptions = [];

    public Task<IReadOnlyList<EmployeeScheduleException>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmployeeScheduleException>>(_exceptions.Where(e => e.EmployeeId == employeeId).ToList());

    public Task<IReadOnlyList<EmployeeScheduleException>> GetByEmployeeIdsAndDateAsync(IEnumerable<Guid> employeeIds, DateOnly date, CancellationToken ct = default)
    {
        var ids = employeeIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<EmployeeScheduleException>>(
            _exceptions.Where(e => ids.Contains(e.EmployeeId) && e.Date == date).ToList());
    }

    public Task ReplaceForEmployeeAsync(Guid employeeId, IEnumerable<EmployeeScheduleException> exceptions, CancellationToken ct = default)
    {
        _exceptions.RemoveAll(e => e.EmployeeId == employeeId);
        _exceptions.AddRange(exceptions);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryIntegrationConnectionRepository : IIntegrationConnectionRepository
{
    private readonly List<IntegrationConnection> _connections = [];

    public Task<IReadOnlyList<IntegrationConnection>> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntegrationConnection>>(_connections.Where(c => c.BusinessId == businessId).ToList());

    public Task<IntegrationConnection?> GetByBusinessProviderCapabilityAsync(
        Guid businessId,
        Domain.Enums.IntegrationProvider provider,
        Domain.Enums.IntegrationCapability capability,
        CancellationToken ct = default) =>
        Task.FromResult(_connections.FirstOrDefault(c =>
            c.BusinessId == businessId && c.Provider == provider && c.Capability == capability));

    public Task<IntegrationConnection> CreateAsync(IntegrationConnection connection, CancellationToken ct = default)
    {
        _connections.Add(connection);
        return Task.FromResult(connection);
    }

    public Task<IntegrationConnection> UpdateAsync(IntegrationConnection connection, CancellationToken ct = default) =>
        Task.FromResult(connection);
}

internal sealed class InMemoryReservationIntegrationEventRepository : IReservationIntegrationEventRepository
{
    private readonly List<ReservationIntegrationEvent> _events = [];

    public Task<ReservationIntegrationEvent?> GetByReservationAndConnectionAsync(Guid reservationId, Guid integrationConnectionId, CancellationToken ct = default) =>
        Task.FromResult(_events.FirstOrDefault(e => e.ReservationId == reservationId && e.IntegrationConnectionId == integrationConnectionId));

    public Task<IReadOnlyList<ReservationIntegrationEvent>> GetByReservationIdAsync(Guid reservationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReservationIntegrationEvent>>(_events.Where(e => e.ReservationId == reservationId).ToList());

    public Task<ReservationIntegrationEvent> AddAsync(ReservationIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        _events.Add(integrationEvent);
        return Task.FromResult(integrationEvent);
    }

    public Task<ReservationIntegrationEvent> UpdateAsync(ReservationIntegrationEvent integrationEvent, CancellationToken ct = default) =>
        Task.FromResult(integrationEvent);
}
