namespace MimosBabySpa.Domain.Repositories;

public interface IUnitOfWork : IDisposable
{
    IConversationRepository Conversations { get; }
    IMessageRepository Messages { get; }
    ILeadRepository Leads { get; }
    ICampaignRepository Campaigns { get; }
    IBusinessRepository Businesses { get; }
    IBusinessWhatsAppNumberRepository BusinessWhatsAppNumbers { get; }
    ISystemConfigurationRepository SystemConfigurations { get; }
    IConversationContextRepository ConversationContexts { get; }
    ICustomerMemoryRepository CustomerMemory { get; }
    IReservationRepository Reservations { get; }
    IServiceRepository Services { get; }
    IServiceCategoryRepository ServiceCategories { get; }
    IBusinessAttachmentRepository BusinessAttachments { get; }
    IBusinessResourceRepository BusinessResources { get; }
    IEmployeeRepository Employees { get; }
    IEmployeeServiceRepository EmployeeServices { get; }
    IBusinessWorkingHourRepository BusinessWorkingHours { get; }
    IEmployeeWorkingHourRepository EmployeeWorkingHours { get; }
    IEmployeeScheduleExceptionRepository EmployeeScheduleExceptions { get; }
    IBusinessSchedulingSettingsRepository BusinessSchedulingSettings { get; }
    IBusinessAvailabilityBlockRepository BusinessAvailabilityBlocks { get; }
    IScheduledAutomationJobRepository ScheduledAutomationJobs { get; }
    IReservationAttendanceResponseRepository ReservationAttendanceResponses { get; }
    IIntegrationConnectionRepository IntegrationConnections { get; }
    IReservationIntegrationEventRepository ReservationIntegrationEvents { get; }
    IExternalEscalationAttemptRepository ExternalEscalationAttempts { get; }
    IExternalEscalationOutcomeDeliveryRepository ExternalEscalationOutcomeDeliveries { get; }
    IConversationStateRepository ConversationStates { get; }
    IServiceAddOnRuleRepository ServiceAddOnRules { get; }
    IReservationAddOnRepository ReservationAddOns { get; }
    IProductRepository Products { get; }
    IProductAliasRepository ProductAliases { get; }
    ICartMutationReceiptRepository CartMutationReceipts { get; }
    IProductRecommendationRuleRepository ProductRecommendationRules { get; }
    IPromotionRepository Promotions { get; }
    IOrderDraftRepository OrderDrafts { get; }
    IOrderDraftItemRepository OrderDraftItems { get; }
    IOrderRepository Orders { get; }
    IOrderItemRepository OrderItems { get; }
    IOrderConnectionEventRepository OrderConnectionEvents { get; }

    IPaymentTransactionRepository PaymentTransactions { get; }
    IEnrollmentRepository Enrollments { get; }
    IAppUserRepository AppUsers { get; }
    IAppRoleRepository AppRoles { get; }
    IPermissionRepository Permissions { get; }
    IUserRoleRepository UserRoles { get; }
    IRolePermissionRepository RolePermissions { get; }
    IRefreshTokenRepository RefreshTokens { get; }
    IUserExternalLoginRepository UserExternalLogins { get; }
    IAuditLogRepository AuditLogs { get; }
    ITenantRepository Tenants { get; }
    ISubscriptionPlanRepository SubscriptionPlans { get; }
    IBusinessSubscriptionRepository BusinessSubscriptions { get; }
    IBusinessUsagePeriodRepository BusinessUsagePeriods { get; }
    IUsageLedgerRepository UsageLedger { get; }
    IUsageCostRateRepository UsageCostRates { get; }
    IAgentTemplateRepository AgentTemplates { get; }
    IBusinessInboundContactRepository BusinessInboundContacts { get; }

    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);

    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
