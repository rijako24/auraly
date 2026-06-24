using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
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
    public IBusinessSchedulingSettingsRepository BusinessSchedulingSettings { get; }
    public IScheduledAutomationJobRepository ScheduledAutomationJobs { get; }
    public IReservationAttendanceResponseRepository ReservationAttendanceResponses { get; }
    public IIntegrationConnectionRepository IntegrationConnections { get; }
    public IReservationIntegrationEventRepository ReservationIntegrationEvents { get; }
    public IExternalEscalationAttemptRepository ExternalEscalationAttempts { get; }
    public ILeadRepository Leads { get; }
    public IServiceAddOnRuleRepository ServiceAddOnRules { get; }
    public IReservationAddOnRepository ReservationAddOns { get; }
    public IProductRepository Products { get; }
    public IPromotionRepository Promotions { get; }
    public IOrderDraftRepository OrderDrafts { get; }
    public IOrderDraftItemRepository OrderDraftItems { get; }
    public IOrderRepository Orders { get; }
    public IOrderItemRepository OrderItems { get; }
    public IOrderConnectionEventRepository OrderConnectionEvents { get; }
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
        BusinessSchedulingSettings = new InMemoryBusinessSchedulingSettingsRepository(businessId);
        ScheduledAutomationJobs = new InMemoryScheduledAutomationJobRepository();
        ReservationAttendanceResponses = new InMemoryReservationAttendanceResponseRepository();
        IntegrationConnections = new InMemoryIntegrationConnectionRepository();
        ReservationIntegrationEvents = new InMemoryReservationIntegrationEventRepository();
        ExternalEscalationAttempts = new InMemoryExternalEscalationAttemptRepository();
        Leads                = new InMemoryLeadRepository();
        ServiceAddOnRules    = new InMemoryServiceAddOnRuleRepository(businessId);
        ReservationAddOns    = new InMemoryReservationAddOnRepository();
        Products             = new InMemoryProductRepository();
        Promotions           = new InMemoryPromotionRepository();
        OrderDrafts          = new InMemoryOrderDraftRepository();
        OrderDraftItems      = new InMemoryOrderDraftItemRepository();
        Orders               = new InMemoryOrderRepository();
        OrderItems           = new InMemoryOrderItemRepository();
        OrderConnectionEvents = new InMemoryOrderConnectionEventRepository();
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

    public Task<IReadOnlyList<IntegrationConnection>> GetByBusinessConnectionTypeAsync(
        Guid businessId,
        ConnectionType connectionType,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntegrationConnection>>(
            _connections.Where(c => c.BusinessId == businessId && c.ConnectionType == connectionType).ToList());

    public Task<IntegrationConnection?> GetByBusinessProviderCapabilityAsync(
        Guid businessId,
        IntegrationProvider provider,
        IntegrationCapability capability,
        CancellationToken ct = default) =>
        Task.FromResult(_connections.FirstOrDefault(c =>
            c.BusinessId == businessId &&
            c.Provider == (int)provider &&
            c.Capability == (int)capability));

    public Task<IntegrationConnection?> GetCommerceConnectionAsync(
        Guid businessId,
        CommerceProvider provider,
        CommerceCapability capability = CommerceCapability.CatalogAndOrders,
        CancellationToken ct = default) =>
        Task.FromResult(_connections.FirstOrDefault(c =>
            c.BusinessId == businessId &&
            c.ConnectionType == ConnectionType.Commerce &&
            c.Provider == (int)provider &&
            c.Capability == (int)capability));

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

internal sealed class InMemoryExternalEscalationAttemptRepository : IExternalEscalationAttemptRepository
{
    private readonly List<ExternalEscalationAttempt> _escalations = [];

    public Task<ExternalEscalationAttempt?> GetByIdAsync(Guid attemptId, CancellationToken ct = default) =>
        Task.FromResult(_escalations.FirstOrDefault(o => o.ExternalEscalationAttemptId == attemptId));

    public Task<ExternalEscalationAttempt?> GetByAttemptCodeAsync(Guid businessId, string attemptCode, string phone, CancellationToken ct = default) =>
        Task.FromResult(_escalations.FirstOrDefault(o =>
            o.BusinessId == businessId &&
            o.AttemptCode == attemptCode &&
            NormalizePhone(o.ContactPhoneSnapshot) == NormalizePhone(phone)));

    public Task<ExternalEscalationAttempt?> GetByWhatsAppMessageIdAsync(Guid businessId, string whatsAppMessageId, string phone, CancellationToken ct = default) =>
        Task.FromResult(_escalations.FirstOrDefault(o =>
            o.BusinessId == businessId &&
            o.WhatsAppMessageId == whatsAppMessageId &&
            NormalizePhone(o.ContactPhoneSnapshot) == NormalizePhone(phone)));

    public Task<IReadOnlyList<ExternalEscalationAttempt>> GetPendingByContactPhoneAsync(Guid businessId, string phone, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExternalEscalationAttempt>>(
            _escalations.Where(o => o.BusinessId == businessId &&
                               NormalizePhone(o.ContactPhoneSnapshot) == NormalizePhone(phone) &&
                               o.Status == ExternalEscalationAttemptStatus.Pending)
                   .ToList());

    public Task<IReadOnlyList<ExternalEscalationAttempt>> GetExpiredPendingAttemptsAsync(DateTime utcNow, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExternalEscalationAttempt>>(
            _escalations.Where(o => o.Status == ExternalEscalationAttemptStatus.Pending && o.ExpiresAt <= utcNow).ToList());

    public Task<int> CountAttemptsAsync(Guid businessId, string eventName, string targetType, Guid targetId, CancellationToken ct = default) =>
        Task.FromResult(_escalations.Count(o =>
            o.BusinessId == businessId &&
            o.EventName == eventName &&
            o.TargetType == targetType &&
            o.TargetId == targetId));

    public Task<bool> HasAcceptedForTargetAsync(Guid businessId, string eventName, string targetType, Guid targetId, CancellationToken ct = default) =>
        Task.FromResult(_escalations.Any(o =>
            o.BusinessId == businessId &&
            o.EventName == eventName &&
            o.TargetType == targetType &&
            o.TargetId == targetId &&
            o.Status == ExternalEscalationAttemptStatus.Accepted));

    public Task<ExternalEscalationAttempt> AddAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default)
    {
        _escalations.Add(attempt);
        return Task.FromResult(attempt);
    }

    public Task<ExternalEscalationAttempt> UpdateAsync(ExternalEscalationAttempt attempt, CancellationToken ct = default) =>
        Task.FromResult(attempt);

    public Task CancelPendingForTargetAsync(Guid businessId, string eventName, string targetType, Guid targetId, Guid exceptOfferId, CancellationToken ct = default)
    {
        foreach (var attempt in _escalations.Where(o =>
                     o.BusinessId == businessId &&
                     o.EventName == eventName &&
                     o.TargetType == targetType &&
                     o.TargetId == targetId &&
                     o.ExternalEscalationAttemptId != exceptOfferId &&
                     o.Status == ExternalEscalationAttemptStatus.Pending))
        {
            attempt.Status = ExternalEscalationAttemptStatus.Cancelled;
            attempt.CancelledAt = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    private static string NormalizePhone(string phone) =>
        new(phone.Where(char.IsDigit).ToArray());
}

internal sealed class InMemoryProductRepository : IProductRepository
{
    private readonly List<Product> _products = [];

    public Task<IReadOnlyList<Product>> SearchAsync(Guid businessId, string? query, string? category, int limit, CancellationToken ct = default)
    {
        var results = _products.Where(p => p.BusinessId == businessId && p.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
        {
            results = results.Where(p =>
                CatalogSearchText.ContainsAllTerms(query, p.Name, p.Description, p.Sku, p.CategoryName));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            results = results.Where(p => CatalogSearchText.ContainsAllTerms(category, p.CategoryName));
        }

        return Task.FromResult<IReadOnlyList<Product>>(results.Take(limit).ToList());
    }

    public Task<Product?> GetByIdAsync(Guid businessId, Guid productId, CancellationToken ct = default) =>
        Task.FromResult(_products.FirstOrDefault(p => p.BusinessId == businessId && p.ProductId == productId));

    public Task<Product?> GetByExternalIdAsync(Guid businessId, Guid integrationConnectionId, string externalProductId, CancellationToken ct = default) =>
        Task.FromResult(_products.FirstOrDefault(p =>
            p.BusinessId == businessId &&
            p.IntegrationConnectionId == integrationConnectionId &&
            p.ExternalProductId == externalProductId));

    public Task<Product> CreateAsync(Product product, CancellationToken ct = default)
    {
        _products.Add(product);
        return Task.FromResult(product);
    }

    public Task<Product> UpdateAsync(Product product, CancellationToken ct = default) =>
        Task.FromResult(product);
}

internal sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly List<Order> _orders = [];

    public Task<Order?> GetByIdAsync(Guid businessId, Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_orders.FirstOrDefault(o => o.BusinessId == businessId && o.OrderId == orderId));

    public Task<Order?> GetByPaymentTransactionIdAsync(Guid businessId, Guid paymentTransactionId, CancellationToken ct = default) =>
        Task.FromResult(_orders.FirstOrDefault(o => o.BusinessId == businessId && o.PaymentTransactionId == paymentTransactionId));

    public Task<Order?> GetActiveDraftByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult(GetActiveDrafts(businessId, conversationId).FirstOrDefault());

    public Task<IReadOnlyList<Order>> GetActiveDraftsByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Order>>(GetActiveDrafts(businessId, conversationId));

    private List<Order> GetActiveDrafts(Guid businessId, Guid conversationId) =>
        _orders
            .Where(o =>
                o.BusinessId == businessId &&
                o.ConversationId == conversationId &&
                (o.Status == OrderStatus.Draft || o.Status == OrderStatus.PendingConfirmation || o.Status == OrderStatus.AwaitingPayment))
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

    public Task<IReadOnlyList<Order>> GetByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Order>>(
            _orders.Where(o => o.BusinessId == businessId && o.ConversationId == conversationId).ToList());

    public Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search,
        string? customer,
        DateTime? createdFrom,
        DateTime? createdTo,
        OrderStatus? status,
        CancellationToken ct = default)
    {
        var query = _orders.Where(o => o.BusinessId == businessId);
        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        var list = query.ToList();
        return Task.FromResult<(IReadOnlyList<Order> Items, int TotalCount)>(
            (list.Skip(Math.Max(0, page - 1) * pageSize).Take(pageSize).ToList(), list.Count));
    }

    public Task<(int TotalOrders, decimal TotalAmount, int DraftCount, int AwaitingPaymentCount, int ConfirmedCount, int SyncedCount, int CancelledCount)> GetSummaryByBusinessIdAsync(
        Guid businessId,
        string? search,
        string? customer,
        DateTime? createdFrom,
        DateTime? createdTo,
        OrderStatus? status,
        CancellationToken ct = default)
    {
        var orders = _orders.Where(o => o.BusinessId == businessId).ToList();
        return Task.FromResult((
            orders.Count,
            orders.Sum(o => o.Total),
            orders.Count(o => o.Status == OrderStatus.Draft),
            orders.Count(o => o.Status == OrderStatus.AwaitingPayment),
            orders.Count(o => o.Status == OrderStatus.Confirmed),
            orders.Count(o => o.Status == OrderStatus.Synced),
            orders.Count(o => o.Status == OrderStatus.Cancelled)));
    }

    public Task<Order> CreateAsync(Order order, CancellationToken ct = default)
    {
        _orders.Add(order);
        return Task.FromResult(order);
    }

    public Task<Order> UpdateAsync(Order order, CancellationToken ct = default) =>
        Task.FromResult(order);
}

internal sealed class InMemoryOrderItemRepository : IOrderItemRepository
{
    private readonly List<OrderItem> _items = [];

    public Task<IReadOnlyList<OrderItem>> GetByOrderIdAsync(Guid businessId, Guid orderId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrderItem>>(
            _items.Where(i => i.BusinessId == businessId && i.OrderId == orderId).ToList());

    public Task<OrderItem?> GetByIdAsync(Guid businessId, Guid orderItemId, CancellationToken ct = default) =>
        Task.FromResult(_items.FirstOrDefault(i => i.BusinessId == businessId && i.OrderItemId == orderItemId));

    public Task<OrderItem> CreateAsync(OrderItem item, CancellationToken ct = default)
    {
        _items.Add(item);
        return Task.FromResult(item);
    }

    public Task<OrderItem> UpdateAsync(OrderItem item, CancellationToken ct = default) =>
        Task.FromResult(item);

    public Task DeleteAsync(OrderItem item, CancellationToken ct = default)
    {
        _items.Remove(item);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryOrderDraftRepository : IOrderDraftRepository
{
    private readonly List<OrderDraft> _drafts = [];

    public Task<OrderDraft?> GetActiveByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult(_drafts
            .Where(d => d.BusinessId == businessId && d.ConversationId == conversationId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault());

    public Task<OrderDraft?> GetByPaymentTransactionIdAsync(Guid businessId, Guid paymentTransactionId, CancellationToken ct = default) =>
        Task.FromResult(_drafts.FirstOrDefault(d => d.BusinessId == businessId && d.PaymentTransactionId == paymentTransactionId));

    public Task<IReadOnlyList<OrderDraft>> GetActiveDraftsByConversationAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrderDraft>>(_drafts
            .Where(d => d.BusinessId == businessId && d.ConversationId == conversationId)
            .OrderByDescending(d => d.CreatedAt)
            .ToList());

    public Task<OrderDraft> CreateAsync(OrderDraft draft, CancellationToken ct = default)
    {
        _drafts.Add(draft);
        return Task.FromResult(draft);
    }

    public Task<OrderDraft> UpdateAsync(OrderDraft draft, CancellationToken ct = default) =>
        Task.FromResult(draft);

    public Task DeleteAsync(OrderDraft draft, CancellationToken ct = default)
    {
        _drafts.Remove(draft);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryOrderDraftItemRepository : IOrderDraftItemRepository
{
    private readonly List<OrderDraftItem> _items = [];

    public Task<IReadOnlyList<OrderDraftItem>> GetByDraftIdAsync(Guid businessId, Guid orderDraftId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrderDraftItem>>(
            _items.Where(i => i.BusinessId == businessId && i.OrderDraftId == orderDraftId).ToList());

    public Task<OrderDraftItem?> GetByIdAsync(Guid businessId, Guid orderDraftItemId, CancellationToken ct = default) =>
        Task.FromResult(_items.FirstOrDefault(i => i.BusinessId == businessId && i.OrderDraftItemId == orderDraftItemId));

    public Task<OrderDraftItem> CreateAsync(OrderDraftItem item, CancellationToken ct = default)
    {
        _items.Add(item);
        return Task.FromResult(item);
    }

    public Task<OrderDraftItem> UpdateAsync(OrderDraftItem item, CancellationToken ct = default) =>
        Task.FromResult(item);

    public Task DeleteAsync(OrderDraftItem item, CancellationToken ct = default)
    {
        _items.Remove(item);
        return Task.CompletedTask;
    }
}
internal sealed class InMemoryOrderConnectionEventRepository : IOrderConnectionEventRepository
{
    private readonly List<OrderConnectionEvent> _events = [];

    public Task<OrderConnectionEvent?> GetByOrderConnectionAsync(Guid orderId, Guid integrationConnectionId, CancellationToken ct = default) =>
        Task.FromResult(_events.FirstOrDefault(e => e.OrderId == orderId && e.IntegrationConnectionId == integrationConnectionId));

    public Task<OrderConnectionEvent> CreateAsync(OrderConnectionEvent entity, CancellationToken ct = default)
    {
        _events.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<OrderConnectionEvent> UpdateAsync(OrderConnectionEvent entity, CancellationToken ct = default) =>
        Task.FromResult(entity);
}
