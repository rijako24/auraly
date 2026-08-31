CREATE TABLE billing.BillableServices
(
    BillableServiceId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BillableServices PRIMARY KEY,
    BusinessId UNIQUEIDENTIFIER NOT NULL,
    Code NVARCHAR(48) NOT NULL,
    Name NVARCHAR(120) NOT NULL,
    Description NVARCHAR(500) NULL,
    UnitLabel NVARCHAR(80) NOT NULL,
    UblUnitCode NVARCHAR(8) NOT NULL CONSTRAINT DF_BillableServices_UblUnitCode DEFAULT N'94',
    UnitSize INT NOT NULL,
    CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_BillableServices_Currency DEFAULT 'COP',
    UnitPrice DECIMAL(19,4) NOT NULL,
    SalesTaxProfileId UNIQUEIDENTIFIER NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_BillableServices_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_BillableServices_Business FOREIGN KEY(BusinessId) REFERENCES dbo.Businesses(BusinessId),
    CONSTRAINT FK_BillableServices_SalesTax FOREIGN KEY(SalesTaxProfileId) REFERENCES dbo.TaxProfiles(TaxProfileId),
    CONSTRAINT UQ_BillableServices_Business_Code UNIQUE(BusinessId,Code),
    CONSTRAINT CK_BillableServices_Values CHECK(UnitSize>0 AND UnitPrice>=0)
);
GO

CREATE TABLE billing.TenantCommercialPlans
(
    TenantCommercialPlanId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantCommercialPlans PRIMARY KEY,
    BillableServiceId UNIQUEIDENTIFIER NOT NULL,
    AnnualDiscountRate DECIMAL(9,6) NOT NULL CONSTRAINT DF_TenantCommercialPlans_AnnualDiscount DEFAULT 0.15,
    IncludedFullUsers INT NOT NULL,
    IncludedSellerUsers INT NOT NULL,
    IncludedPosDevices INT NOT NULL,
    IncludedDianDocuments INT NOT NULL,
    IncludedPayrollEmployees INT NOT NULL,
    IsRecommended BIT NOT NULL CONSTRAINT DF_TenantCommercialPlans_Recommended DEFAULT 0,
    IsCustom BIT NOT NULL CONSTRAINT DF_TenantCommercialPlans_Custom DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_TenantCommercialPlans_Active DEFAULT 1,
    FeaturesJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_TenantCommercialPlans_Features DEFAULT N'[]',
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_TenantCommercialPlans_Service FOREIGN KEY(BillableServiceId) REFERENCES billing.BillableServices(BillableServiceId),
    CONSTRAINT UQ_TenantCommercialPlans_Service UNIQUE(BillableServiceId),
    CONSTRAINT CK_TenantCommercialPlans_Price CHECK(AnnualDiscountRate BETWEEN 0 AND 1),
    CONSTRAINT CK_TenantCommercialPlans_Capacity CHECK(IncludedFullUsers>=0 AND IncludedSellerUsers>=0 AND IncludedPosDevices>=0 AND IncludedDianDocuments>=0 AND IncludedPayrollEmployees>=0)
);
GO

CREATE TABLE billing.TenantCommercialAddOns
(
    TenantCommercialAddOnId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantCommercialAddOns PRIMARY KEY,
    BillableServiceId UNIQUEIDENTIFIER NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_TenantCommercialAddOns_Active DEFAULT 1,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_TenantCommercialAddOns_Service FOREIGN KEY(BillableServiceId) REFERENCES billing.BillableServices(BillableServiceId),
    CONSTRAINT UQ_TenantCommercialAddOns_Service UNIQUE(BillableServiceId)
);
GO

CREATE TABLE billing.TenantProvisioningDrafts
(
    TenantProvisioningDraftId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantProvisioningDrafts PRIMARY KEY,
    OwnerUserId UNIQUEIDENTIFIER NULL,
    OwnerEmail NVARCHAR(256) NOT NULL,
    AccessTokenHash BINARY(32) NOT NULL,
    PayloadJson NVARCHAR(MAX) NOT NULL,
    Status NVARCHAR(32) NOT NULL,
    ExpiresAt DATETIMEOFFSET(7) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    ProvisionedTenantId UNIQUEIDENTIFIER NULL,
    PaymentTransactionId UNIQUEIDENTIFIER NULL,
    ErrorMessage NVARCHAR(1000) NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT FK_TenantProvisioningDrafts_Owner FOREIGN KEY(OwnerUserId) REFERENCES dbo.AppUsers(UserId),
    CONSTRAINT FK_TenantProvisioningDrafts_Tenant FOREIGN KEY(ProvisionedTenantId) REFERENCES dbo.Tenants(TenantId),
    CONSTRAINT FK_TenantProvisioningDrafts_Payment FOREIGN KEY(PaymentTransactionId) REFERENCES dbo.PaymentTransactions(PaymentTransactionId),
    CONSTRAINT CK_TenantProvisioningDrafts_Status CHECK(Status IN(N'Draft',N'Quoted',N'PaymentPending',N'Paid',N'Waived',N'Provisioning',N'BillingCustomerPending',N'Provisioned',N'PaymentFailed',N'Expired',N'Failed'))
);
GO

CREATE UNIQUE INDEX UX_TenantProvisioningDrafts_AccessTokenHash ON billing.TenantProvisioningDrafts(AccessTokenHash);
GO

CREATE TABLE billing.PlatformBillingSettings
(
    PlatformBillingSettingId TINYINT NOT NULL CONSTRAINT PK_PlatformBillingSettings PRIMARY KEY,
    BillingBusinessId UNIQUEIDENTIFIER NOT NULL,
    EmailRemindersEnabled BIT NOT NULL CONSTRAINT DF_PlatformBillingSettings_EmailReminders DEFAULT 1,
    PreDueReminderDays INT NOT NULL CONSTRAINT DF_PlatformBillingSettings_PreDue DEFAULT 5,
    OverdueReminderIntervalDays INT NOT NULL CONSTRAINT DF_PlatformBillingSettings_OverdueInterval DEFAULT 3,
    GracePeriodDays INT NOT NULL CONSTRAINT DF_PlatformBillingSettings_Grace DEFAULT 10,
    BillingTimeZoneId NVARCHAR(100) NOT NULL CONSTRAINT DF_PlatformBillingSettings_TimeZone DEFAULT N'America/Bogota',
    UpdatedByUserId UNIQUEIDENTIFIER NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT FK_PlatformBillingSettings_Business FOREIGN KEY(BillingBusinessId) REFERENCES dbo.Businesses(BusinessId),
    CONSTRAINT FK_PlatformBillingSettings_User FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.AppUsers(UserId),
    CONSTRAINT CK_PlatformBillingSettings_Singleton CHECK(PlatformBillingSettingId=1),
    CONSTRAINT CK_PlatformBillingSettings_Reminders CHECK(PreDueReminderDays BETWEEN 0 AND 90 AND OverdueReminderIntervalDays BETWEEN 1 AND 30 AND GracePeriodDays BETWEEN 1 AND 90),
    CONSTRAINT CK_PlatformBillingSettings_TimeZone CHECK(LEN(LTRIM(RTRIM(BillingTimeZoneId)))>0)
);
GO

CREATE TABLE billing.TenantProvisioningQuotes
(
    TenantProvisioningQuoteId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantProvisioningQuotes PRIMARY KEY,
    TenantProvisioningDraftId UNIQUEIDENTIFIER NOT NULL,
    TenantCommercialPlanId UNIQUEIDENTIFIER NOT NULL,
    BillingPeriod NVARCHAR(16) NOT NULL,
    CurrencyCode CHAR(3) NOT NULL,
    MonthlySubtotal DECIMAL(19,4) NOT NULL,
    Periods INT NOT NULL,
    DiscountRate DECIMAL(9,6) NOT NULL,
    DiscountAmount DECIMAL(19,4) NOT NULL,
    TaxAmount DECIMAL(19,4) NOT NULL,
    PayableAmount DECIMAL(19,4) NOT NULL,
    LinesJson NVARCHAR(MAX) NOT NULL,
    QuoteHash BINARY(32) NOT NULL,
    ExpiresAt DATETIMEOFFSET(7) NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_TenantProvisioningQuotes_Draft FOREIGN KEY(TenantProvisioningDraftId) REFERENCES billing.TenantProvisioningDrafts(TenantProvisioningDraftId),
    CONSTRAINT FK_TenantProvisioningQuotes_Plan FOREIGN KEY(TenantCommercialPlanId) REFERENCES billing.TenantCommercialPlans(TenantCommercialPlanId),
    CONSTRAINT UQ_TenantProvisioningQuotes_Draft UNIQUE(TenantProvisioningDraftId),
    CONSTRAINT CK_TenantProvisioningQuotes_Period CHECK(BillingPeriod IN(N'Monthly',N'Annual') AND Periods IN(1,12)),
    CONSTRAINT CK_TenantProvisioningQuotes_Currency CHECK(CurrencyCode='COP'),
    CONSTRAINT CK_TenantProvisioningQuotes_Amounts CHECK(MonthlySubtotal>=0 AND DiscountRate BETWEEN 0 AND 1 AND DiscountAmount>=0 AND TaxAmount>=0 AND PayableAmount>=0)
);
GO

CREATE TABLE billing.TenantSubscriptions
(
    TenantSubscriptionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantSubscriptions PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    TenantCommercialPlanId UNIQUEIDENTIFIER NOT NULL,
    BillingCustomerId UNIQUEIDENTIFIER NOT NULL,
    BillingPeriod NVARCHAR(16) NOT NULL,
    Status NVARCHAR(24) NOT NULL,
    CurrentPeriodStart DATETIMEOFFSET(7) NOT NULL,
    CurrentPeriodEnd DATETIMEOFFSET(7) NOT NULL,
    BillingAnchorDay TINYINT NOT NULL,
    FullUserLimit INT NOT NULL,
    SellerUserLimit INT NOT NULL,
    PosDeviceLimit INT NOT NULL,
    DianDocumentMonthlyLimit INT NOT NULL,
    PayrollEmployeeLimit INT NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT FK_TenantSubscriptions_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(TenantId),
    CONSTRAINT FK_TenantSubscriptions_Plan FOREIGN KEY(TenantCommercialPlanId) REFERENCES billing.TenantCommercialPlans(TenantCommercialPlanId),
    CONSTRAINT FK_TenantSubscriptions_BillingCustomer FOREIGN KEY(BillingCustomerId) REFERENCES dbo.Customers(CustomerId),
    CONSTRAINT UQ_TenantSubscriptions_Tenant UNIQUE(TenantId),
    CONSTRAINT CK_TenantSubscriptions_Period CHECK(BillingPeriod IN(N'Monthly',N'Annual')),
    CONSTRAINT CK_TenantSubscriptions_Status CHECK(Status IN(N'Active',N'PastDue',N'Suspended',N'Cancelled')),
    CONSTRAINT CK_TenantSubscriptions_Limits CHECK(FullUserLimit>=0 AND SellerUserLimit>=0 AND PosDeviceLimit>=0 AND DianDocumentMonthlyLimit>=0 AND PayrollEmployeeLimit>=0),
    CONSTRAINT CK_TenantSubscriptions_Anchor CHECK(BillingAnchorDay BETWEEN 1 AND 31)
);
GO

CREATE TABLE billing.TenantSubscriptionUsagePeriods
(
    TenantSubscriptionUsagePeriodId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantSubscriptionUsagePeriods PRIMARY KEY,
    TenantSubscriptionId UNIQUEIDENTIFIER NOT NULL,
    PeriodStart DATETIMEOFFSET(7) NOT NULL,
    PeriodEnd DATETIMEOFFSET(7) NOT NULL,
    DianDocumentsUsed INT NOT NULL CONSTRAINT DF_TenantSubscriptionUsagePeriods_DianUsed DEFAULT 0,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT FK_TenantSubscriptionUsagePeriods_Subscription FOREIGN KEY(TenantSubscriptionId) REFERENCES billing.TenantSubscriptions(TenantSubscriptionId),
    CONSTRAINT UQ_TenantSubscriptionUsagePeriods_Period UNIQUE(TenantSubscriptionId,PeriodStart),
    CONSTRAINT CK_TenantSubscriptionUsagePeriods_Usage CHECK(DianDocumentsUsed>=0 AND PeriodEnd>PeriodStart)
);
GO

CREATE TABLE billing.TenantSubscriptionRenewalOrders
(
    TenantSubscriptionRenewalOrderId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantSubscriptionRenewalOrders PRIMARY KEY,
    TenantSubscriptionId UNIQUEIDENTIFIER NOT NULL,
    Revision INT NOT NULL,
    IsCurrent BIT NOT NULL,
    Status NVARCHAR(24) NOT NULL,
    TargetPeriodStart DATETIMEOFFSET(7) NOT NULL,
    TargetPeriodEnd DATETIMEOFFSET(7) NOT NULL,
    DueAt DATETIMEOFFSET(7) NOT NULL,
    TenantCommercialPlanId UNIQUEIDENTIFIER NOT NULL,
    BillingPeriod NVARCHAR(16) NOT NULL,
    CurrencyCode CHAR(3) NOT NULL,
    MonthlySubtotal DECIMAL(19,4) NOT NULL,
    Periods INT NOT NULL,
    DiscountRate DECIMAL(9,6) NOT NULL,
    DiscountAmount DECIMAL(19,4) NOT NULL,
    TaxAmount DECIMAL(19,4) NOT NULL,
    PayableAmount DECIMAL(19,4) NOT NULL,
    FullUserLimit INT NOT NULL,
    SellerUserLimit INT NOT NULL,
    PosDeviceLimit INT NOT NULL,
    DianDocumentMonthlyLimit INT NOT NULL,
    PayrollEmployeeLimit INT NOT NULL,
    LinesJson NVARCHAR(MAX) NOT NULL,
    OrderHash BINARY(32) NOT NULL,
    PaymentTransactionId UNIQUEIDENTIFIER NULL,
    CreatedByUserId UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT FK_TenantSubscriptionRenewalOrders_Subscription FOREIGN KEY(TenantSubscriptionId) REFERENCES billing.TenantSubscriptions(TenantSubscriptionId),
    CONSTRAINT FK_TenantSubscriptionRenewalOrders_Plan FOREIGN KEY(TenantCommercialPlanId) REFERENCES billing.TenantCommercialPlans(TenantCommercialPlanId),
    CONSTRAINT FK_TenantSubscriptionRenewalOrders_Payment FOREIGN KEY(PaymentTransactionId) REFERENCES dbo.PaymentTransactions(PaymentTransactionId),
    CONSTRAINT FK_TenantSubscriptionRenewalOrders_User FOREIGN KEY(CreatedByUserId) REFERENCES dbo.AppUsers(UserId),
    CONSTRAINT UQ_TenantSubscriptionRenewalOrders_Revision UNIQUE(TenantSubscriptionId,TargetPeriodStart,Revision),
    CONSTRAINT CK_TenantSubscriptionRenewalOrders_Status CHECK(Status IN(N'Draft',N'PendingPayment',N'PaymentConfirmed',N'Invoicing',N'Activated',N'Expired',N'Cancelled',N'PaymentFailed')),
    CONSTRAINT CK_TenantSubscriptionRenewalOrders_Period CHECK(BillingPeriod IN(N'Monthly',N'Annual') AND Periods IN(1,12) AND TargetPeriodEnd>TargetPeriodStart),
    CONSTRAINT CK_TenantSubscriptionRenewalOrders_Currency CHECK(CurrencyCode='COP'),
    CONSTRAINT CK_TenantSubscriptionRenewalOrders_Amounts CHECK(MonthlySubtotal>=0 AND DiscountRate BETWEEN 0 AND 1 AND DiscountAmount>=0 AND TaxAmount>=0 AND PayableAmount>0),
    CONSTRAINT CK_TenantSubscriptionRenewalOrders_Limits CHECK(FullUserLimit>=0 AND SellerUserLimit>=0 AND PosDeviceLimit>=0 AND DianDocumentMonthlyLimit>=0 AND PayrollEmployeeLimit>=0)
);
GO

CREATE UNIQUE INDEX UX_TenantSubscriptionRenewalOrders_Current
  ON billing.TenantSubscriptionRenewalOrders(TenantSubscriptionId,TargetPeriodStart)
  WHERE IsCurrent=1;
GO

CREATE INDEX IX_TenantSubscriptionRenewalOrders_Status_Due
  ON billing.TenantSubscriptionRenewalOrders(Status,DueAt)
  INCLUDE(TenantSubscriptionId,PayableAmount,PaymentTransactionId);
GO

CREATE TABLE billing.TenantBillingNotifications
(
    TenantBillingNotificationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantBillingNotifications PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    TenantSubscriptionRenewalOrderId UNIQUEIDENTIFIER NOT NULL,
    EventKey NVARCHAR(64) NOT NULL,
    Title NVARCHAR(160) NOT NULL,
    Message NVARCHAR(600) NOT NULL,
    ActionUrl NVARCHAR(500) NOT NULL,
    EmailOutboxMessageId UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    ReadAt DATETIMEOFFSET(7) NULL,
    CONSTRAINT FK_TenantBillingNotifications_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(TenantId),
    CONSTRAINT FK_TenantBillingNotifications_User FOREIGN KEY(UserId) REFERENCES dbo.AppUsers(UserId),
    CONSTRAINT FK_TenantBillingNotifications_Order FOREIGN KEY(TenantSubscriptionRenewalOrderId) REFERENCES billing.TenantSubscriptionRenewalOrders(TenantSubscriptionRenewalOrderId),
    CONSTRAINT FK_TenantBillingNotifications_EmailOutbox FOREIGN KEY(EmailOutboxMessageId) REFERENCES dbo.TenantProvisioningOutboxMessages(MessageId),
    CONSTRAINT UQ_TenantBillingNotifications_Event UNIQUE(UserId,TenantSubscriptionRenewalOrderId,EventKey),
    CONSTRAINT UQ_TenantBillingNotifications_Email UNIQUE(EmailOutboxMessageId)
);
GO

CREATE INDEX IX_TenantBillingNotifications_User_Unread
  ON billing.TenantBillingNotifications(TenantId,UserId,ReadAt,CreatedAt DESC)
  INCLUDE(TenantSubscriptionRenewalOrderId,EventKey,Title,ActionUrl);
GO

CREATE TABLE billing.TenantSubscriptionInvoiceLinks
(
    SalesDocumentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantSubscriptionInvoiceLinks PRIMARY KEY,
    TenantSubscriptionId UNIQUEIDENTIFIER NOT NULL,
    TenantSubscriptionRenewalOrderId UNIQUEIDENTIFIER NULL,
    PaymentTransactionId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    EnginesDispatchedAt DATETIMEOFFSET(7) NULL,
    CONSTRAINT FK_TenantSubscriptionInvoiceLinks_Document FOREIGN KEY(SalesDocumentId) REFERENCES dbo.SalesDocuments(DocumentId),
    CONSTRAINT FK_TenantSubscriptionInvoiceLinks_Subscription FOREIGN KEY(TenantSubscriptionId) REFERENCES billing.TenantSubscriptions(TenantSubscriptionId),
    CONSTRAINT FK_TenantSubscriptionInvoiceLinks_Order FOREIGN KEY(TenantSubscriptionRenewalOrderId) REFERENCES billing.TenantSubscriptionRenewalOrders(TenantSubscriptionRenewalOrderId),
    CONSTRAINT FK_TenantSubscriptionInvoiceLinks_Payment FOREIGN KEY(PaymentTransactionId) REFERENCES dbo.PaymentTransactions(PaymentTransactionId),
    CONSTRAINT UQ_TenantSubscriptionInvoiceLinks_Order UNIQUE(TenantSubscriptionRenewalOrderId),
    CONSTRAINT UQ_TenantSubscriptionInvoiceLinks_Payment UNIQUE(PaymentTransactionId)
);
GO

CREATE TABLE billing.TenantDianDocumentUsages
(
    TenantDianDocumentUsageId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantDianDocumentUsages PRIMARY KEY,
    TenantSubscriptionUsagePeriodId UNIQUEIDENTIFIER NOT NULL,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    BusinessId UNIQUEIDENTIFIER NOT NULL,
    SourceDocumentId UNIQUEIDENTIFIER NOT NULL,
    DocumentKind NVARCHAR(32) NOT NULL,
    Status NVARCHAR(16) NOT NULL,
    ReservedAt DATETIMEOFFSET(7) NOT NULL,
    ReleasedAt DATETIMEOFFSET(7) NULL,
    CONSTRAINT FK_TenantDianDocumentUsages_Period FOREIGN KEY(TenantSubscriptionUsagePeriodId) REFERENCES billing.TenantSubscriptionUsagePeriods(TenantSubscriptionUsagePeriodId),
    CONSTRAINT FK_TenantDianDocumentUsages_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(TenantId),
    CONSTRAINT FK_TenantDianDocumentUsages_Business FOREIGN KEY(BusinessId) REFERENCES dbo.Businesses(BusinessId),
    CONSTRAINT UQ_TenantDianDocumentUsages_Document UNIQUE(TenantId,SourceDocumentId,DocumentKind),
    CONSTRAINT CK_TenantDianDocumentUsages_Kind CHECK(DocumentKind IN(N'Invoice',N'SupportDocument',N'ElectronicPayroll')),
    CONSTRAINT CK_TenantDianDocumentUsages_Status CHECK(Status IN(N'Reserved',N'Released'))
);
GO

CREATE INDEX IX_TenantDianDocumentUsages_Period_Status
  ON billing.TenantDianDocumentUsages(TenantSubscriptionUsagePeriodId,Status,ReservedAt);
GO

CREATE INDEX IX_TenantSubscriptions_Status_End ON billing.TenantSubscriptions(Status,CurrentPeriodEnd);
GO
CREATE INDEX IX_TenantProvisioningDrafts_Owner_Status ON billing.TenantProvisioningDrafts(OwnerUserId,Status,UpdatedAt DESC);
GO
