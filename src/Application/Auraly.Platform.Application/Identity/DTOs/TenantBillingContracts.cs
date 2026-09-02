using Auraly.Contracts.Tenants;

namespace Auraly.Contracts.TenantBilling;

public sealed record TenantCommercialPlanDto(
    Guid PlanId, string Code, string Name, decimal MonthlyPriceCop,
    decimal SalesTaxRate, decimal AnnualDiscountRate, int IncludedFullUsers, int IncludedSellerUsers,
    int IncludedPosDevices, int IncludedDianDocuments, int IncludedPayrollEmployees,
    bool IsRecommended, bool IsCustom, IReadOnlyList<string> Features);

public sealed record TenantCommercialAddOnDto(
    Guid AddOnId, string Code, string Name, string UnitLabel, int UnitSize,
    decimal MonthlyUnitPriceCop, decimal SalesTaxRate);

public sealed record TenantCommercialCatalogDto(
    IReadOnlyList<TenantCommercialPlanDto> Plans,
    IReadOnlyList<TenantCommercialAddOnDto> AddOns);

public sealed record TenantQuoteRequest(
    string PlanCode, string BillingPeriod, int AdditionalFullUsers,
    int SellerUsers, int AdditionalPosDevices, int DianDocumentPacks,
    int PayrollEmployeePacks = 0);

public sealed record TenantQuoteLineDto(
    string Code, string Name, int Quantity, int UnitSize,
    decimal MonthlyUnitPriceCop, decimal MonthlyTotalCop, decimal SalesTaxRate);

public sealed record TenantQuoteDto(
    string PlanCode, string PlanName, string BillingPeriod,
    decimal MonthlySubtotalCop, int Periods, decimal GrossPeriodAmountCop,
    decimal DiscountRate, decimal DiscountAmountCop, decimal TaxAmountCop,
    decimal PayableAmountCop, decimal MonthlyEquivalentCop,
    int FullUserLimit, int SellerUserLimit, int PosDeviceLimit,
    int DianDocumentMonthlyLimit, int PayrollEmployeeLimit,
    IReadOnlyList<TenantQuoteLineDto> Lines);

public sealed record WaivedTenantProvisioningRequest(
    ProvisionTenantRequest Tenant,
    TenantQuoteRequest Quote);

public sealed record TenantProvisioningGeographyDto(Guid Id, string Code, string Name);

public sealed record TenantProvisioningLegalIdentityOptionDto(
    string Code, string Label, string? EntityTypeCode = null);

public sealed record TenantProvisioningLegalIdentityCatalogDto(
    IReadOnlyList<TenantProvisioningLegalIdentityOptionDto> EntityTypes,
    IReadOnlyList<TenantProvisioningLegalIdentityOptionDto> IdentificationTypes);

public sealed record TenantCommercialSubscriptionDto(
    Guid SubscriptionId, string PlanCode, string PlanName, string BillingPeriod,
    string Status, DateTimeOffset CurrentPeriodStart, DateTimeOffset CurrentPeriodEnd,
    int FullUserLimit, int SellerUserLimit, int PosDeviceLimit,
    int DianDocumentMonthlyLimit, int DianDocumentsUsed, int PayrollEmployeeLimit);

public sealed record PlatformTenantSubscriptionDto(
    Guid TenantId, string TenantKey, string TenantName, string TenantEmail,
    Guid? SubscriptionId, string? PlanCode, string? PlanName, string? BillingPeriod,
    string? Status, DateTimeOffset? CurrentPeriodStart, DateTimeOffset? CurrentPeriodEnd,
    int? FullUserLimit, int? SellerUserLimit, int? PosDeviceLimit,
    int? DianDocumentMonthlyLimit, int? DianDocumentsUsed, int? PayrollEmployeeLimit,
    Guid? RenewalOrderId, string? RenewalStatus, DateTimeOffset? RenewalDueAt,
    decimal? RenewalPayableAmount);

public sealed record PlatformTenantSubscriptionPageDto(
    IReadOnlyList<PlatformTenantSubscriptionDto> Items,
    int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public sealed record TenantSubscriptionUsageDto(
    int FullUsers, int SellerUsers, int PosDevices, int PayrollEmployees);

public sealed record TenantRenewalOrderDto(
    Guid RenewalOrderId, int Revision, string Status, bool IsCurrent,
    DateTimeOffset TargetPeriodStart, DateTimeOffset TargetPeriodEnd, DateTimeOffset DueAt,
    TenantQuoteDto Quote, TenantSubscriptionUsageDto Usage);

public interface ITenantRenewalOrderStore
{
    Task<TenantRenewalOrderDto?> GetCurrentAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<TenantRenewalOrderDto> CreateRevisionAsync(
        Guid tenantId, Guid userId, TenantQuoteDto quote, CancellationToken cancellationToken);
}

public sealed record TenantSubscriptionLifecycleCandidate(
    Guid ScheduledJobId, Guid SubscriptionId, Guid TenantId, DateTimeOffset CurrentPeriodEnd,
    string PlanCode, string BillingPeriod,
    int FullUserLimit, int SellerUserLimit, int PosDeviceLimit,
    int DianDocumentMonthlyLimit, int PayrollEmployeeLimit,
    int IncludedFullUsers, int IncludedSellerUsers, int IncludedPosDevices,
    int IncludedDianDocuments, int IncludedPayrollEmployees,
    int DianDocumentPackSize, int PayrollEmployeePackSize,
    bool EmailRemindersEnabled, int PreDueReminderDays,
    int OverdueReminderIntervalDays, int GracePeriodDays,
    string BillingTimeZoneId);

public sealed record TenantSubscriptionLifecycleDecision(
    string? SubscriptionStatus, string? EventKey,
    string? Title, string? Message, bool SendEmail,
    DateTimeOffset? NextEvaluationAt);

public interface ITenantSubscriptionLifecycleStore
{
    Task ReconcileSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantSubscriptionLifecycleCandidate>> GetDueAsync(
        DateTimeOffset now, CancellationToken cancellationToken);

    Task ApplyAsync(
        TenantSubscriptionLifecycleCandidate candidate,
        TenantQuoteDto quote,
        TenantSubscriptionLifecycleDecision decision,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record TenantBillingNotificationDto(
    Guid NotificationId, Guid RenewalOrderId, string EventKey,
    string Title, string Message, string ActionUrl,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public interface ITenantBillingNotificationStore
{
    Task<IReadOnlyList<TenantBillingNotificationDto>> GetAsync(
        Guid tenantId, Guid userId, int take, CancellationToken cancellationToken);
    Task MarkReadAsync(
        Guid tenantId, Guid userId, Guid notificationId, CancellationToken cancellationToken);
}

public sealed record PlatformBillingPolicyDto(
    bool EmailRemindersEnabled, int PreDueReminderDays,
    int OverdueReminderIntervalDays, int GracePeriodDays,
    string BillingTimeZoneId, DateTimeOffset UpdatedAt, string Version);

public sealed record UpdatePlatformBillingPolicyRequest(
    bool EmailRemindersEnabled, int PreDueReminderDays,
    int OverdueReminderIntervalDays, int GracePeriodDays,
    string BillingTimeZoneId, string Reason, string Version);

public interface IPlatformBillingPolicyStore
{
    Task<PlatformBillingPolicyDto> GetAsync(CancellationToken cancellationToken);
    Task<PlatformBillingPolicyDto> UpdateAsync(
        Guid actorTenantId, Guid actorUserId,
        UpdatePlatformBillingPolicyRequest request,
        CancellationToken cancellationToken);
}

public interface ITenantCommercialSubscriptionStore
{
    Task<TenantCommercialSubscriptionDto?> GetAsync(
        Guid tenantId, CancellationToken cancellationToken);
    Task<PlatformTenantSubscriptionPageDto> ListPlatformAsync(
        int page, int pageSize, string? search, string? status,
        CancellationToken cancellationToken);
}

public interface ITenantCommercialCatalogStore
{
    Task<TenantCommercialCatalogDto> GetAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetCountriesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetDivisionsAsync(Guid countryId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetCitiesAsync(Guid divisionId, CancellationToken cancellationToken);
    Task<TenantProvisioningLegalIdentityCatalogDto> GetLegalIdentityCatalogAsync(CancellationToken cancellationToken);
}
