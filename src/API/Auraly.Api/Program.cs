using Auraly.Api;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.WebPubSub;
using System.Text;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Domain;

using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Application.Authentication;
using Auraly.Application.Authorization;
using Auraly.Application.Catalog;

using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Parties;
using Auraly.Application.Organization;
using Auraly.Application.Orders;
using Auraly.Application.WorkSessions;
using Auraly.Application.Purchasing;
using Auraly.Application.Payables;
using Auraly.Application.Receivables;
using Auraly.Application.Pricing;
using Auraly.Application.Inventory;
using Auraly.Application.Routes;
using Auraly.Application.Dispatching;
using Auraly.Application.Returns;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.WorkSessions;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Parties;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Fiscal;
using Auraly.Infrastructure.Persistence;
using Auraly.Infrastructure.Pricing;
using Auraly.Infrastructure.Routes;
using Auraly.Infrastructure.Dispatching;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;


var builder = WebApplication.CreateBuilder(args);
builder.AddAuralyPlatformConfiguration();
var connectionString = builder.Configuration.GetConnectionString("Auraly");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Auraly must point to the SQL Server database owned by Auraly.Database.");
}

if (!builder.Environment.IsEnvironment("Testing"))
{
    var fiscalSecretProtectionKey = builder.Configuration[
        "Auraly:Fiscal:SecretProtectionKey"];
    try
    {
        if (string.IsNullOrWhiteSpace(fiscalSecretProtectionKey) ||
            Convert.FromBase64String(fiscalSecretProtectionKey).Length != 32)
            throw new FormatException();
    }
    catch (FormatException)
    {
        throw new InvalidOperationException(
            "Auraly:Fiscal:SecretProtectionKey must be supplied as a Base64-encoded 256-bit key.");
    }
}

builder.AddAuralyPlatformApi(
    configureAuthentication: false,
    configureExternalCustomerMessaging: !builder.Environment.IsEnvironment("Testing"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
builder.Services.AddSingleton(new SqlServerConnectionFactory(connectionString));
builder.Services.AddScoped<SqlExecutionContextDirectory>();
builder.Services.AddScoped<IExecutionAccessResolver>(services =>
    services.GetRequiredService<SqlExecutionContextDirectory>());
builder.Services.AddScoped<IAuralyExecutionContextAccessor, AuralyExecutionContextAccessorAdapter>();
builder.Services.AddSingleton(new AccountingSqlConnectionFactory(connectionString));
builder.Services.AddSingleton(new PricingSqlConnectionFactory(connectionString));
builder.Services.AddSingleton(new RoutesSqlConnectionFactory(connectionString));
builder.Services.AddSingleton(new DispatchingSqlConnectionFactory(connectionString));
builder.Services.AddSingleton<ConfigurationFiscalTechnicalKeyProvider>();
builder.Services.AddSingleton<SqlProtectedFiscalTechnicalKeyStore>();
builder.Services.AddSingleton<IFiscalTechnicalKeySecretWriter>(sp =>
    sp.GetRequiredService<SqlProtectedFiscalTechnicalKeyStore>());
builder.Services.AddSingleton<IFiscalTechnicalKeyProvider, CompositeFiscalTechnicalKeyProvider>();
builder.Services.AddScoped<IFiscalConfigurationStore, SqlFiscalConfigurationStore>();
builder.Services.AddScoped<FiscalConfigurationService>();
builder.Services.AddScoped<IFiscalIssuerConnectionStore, SqlFiscalIssuerConnectionStore>();
builder.Services.AddScoped<FiscalIssuerConnectionService>();
builder.Services.AddScoped<ISalesInvoiceNumberingConfigurationStore,
    SqlSalesInvoiceNumberingConfigurationStore>();
builder.Services.AddScoped<SalesInvoiceNumberingConfigurationService>();
builder.Services.AddScoped<IFiscalSnapshotVerifier, FiscalSnapshotVerifier>();
builder.Services.AddScoped<IFiscalDocumentStore, SqlFiscalDocumentStore>();
builder.Services.AddScoped<FiscalDocumentService>();
builder.Services.AddScoped<IPosFiscalStatusStore, SqlPosFiscalStatusStore>();
builder.Services.AddScoped<PosFiscalStatusService>();
builder.Services.AddScoped<IFiscalGenerationWorkStore, SqlFiscalGenerationWorkStore>();
builder.Services.AddScoped<IFiscalSubmissionWorkStore, SqlFiscalSubmissionWorkStore>();
builder.Services.AddScoped<IDianHabilitationConfigurationProvider,
    SqlDianHabilitationConfigurationProvider>();
builder.Services.AddSingleton<IFiscalSoftwarePinProvider, EnvironmentFiscalSoftwarePinProvider>();
builder.Services.AddSingleton<IFiscalSigningCertificateProvider, WindowsFiscalSigningCertificateProvider>();
builder.Services.AddSingleton<IFiscalXmlSigner, DianXadesSigner>();
builder.Services.AddSingleton<IDianWcfClientFactory, DianWcfClientFactory>();
builder.Services.AddScoped<IDianHabilitationTransport, DianHabilitationTransport>();
builder.Services.AddSingleton<DianInvoiceUblBuilder>();
builder.Services.AddSingleton<DianCreditNoteUblBuilder>();
builder.Services.AddSingleton<DianSchemaValidator>();
builder.Services.AddSingleton<FiscalSubmissionPackageBuilder>();
builder.Services.AddScoped<FiscalGenerationWorker>();
builder.Services.AddScoped<FiscalSubmissionWorker>();
builder.Services.AddScoped<IPosDeviceAuthenticator, SqlPosDeviceAuthenticator>();
builder.Services.AddScoped<IPosSaleServerStore, SqlPosSaleServerStore>();
builder.Services.AddScoped<IPosSaleCustomerResolver, SqlPosSaleCustomerResolver>();
builder.Services.AddScoped<SqlDocumentProcessingSessionAccessor>();
builder.Services.AddScoped<IDocumentProcessingJobStore, SqlDocumentProcessingJobStore>();
builder.Services.AddScoped<IDocumentProcessingWorkSource, SqlDocumentProcessingWorkSource>();
builder.Services.AddScoped<SqlPosSaleDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler>(services => services.GetRequiredService<SqlPosSaleDocumentHandler>());
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlSalesReceiptDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlGoodsReceiptDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlPurchaseReturnDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlPayablePaymentDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlCashReceiptDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlCashDisbursementDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlReceivablePaymentDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlSalesReturnDocumentHandler>();
builder.Services.AddScoped<SqlInventoryOperationProcessor>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlStockCountDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlInventoryDamageDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlInventoryAdjustmentDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlWarehouseTransferDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlProductConversionDocumentHandler>();
builder.Services.AddScoped<DocumentProcessingEngine>();
builder.Services.AddScoped<DocumentProcessingWorker>();
builder.Services.AddScoped<SqlAccountingPostingProcessor>();
builder.Services.AddScoped<IAccountingStore, SqlAccountingStore>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddScoped<IWithholdingRuleStore, SqlWithholdingRuleStore>();
builder.Services.AddScoped<WithholdingEngine>();
builder.Services.AddScoped<WithholdingService>();
builder.Services.AddSingleton<AccountingProcessingCoordinator>();
builder.Services.AddSingleton<FiscalProcessingCoordinator>();
builder.Services.AddScoped<ReceivePosSaleService>();
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<InProcessTestingProcessingTransport>();
    builder.Services.AddSingleton<IDocumentProcessingSignalPublisher>(provider =>
        provider.GetRequiredService<InProcessTestingProcessingTransport>());
    builder.Services.AddSingleton<IFiscalProcessingSignalPublisher>(provider =>
        provider.GetRequiredService<InProcessTestingProcessingTransport>());
    builder.Services.AddSingleton<IAccountingProcessingSignalPublisher>(provider =>
        provider.GetRequiredService<InProcessTestingProcessingTransport>());
}
else
{
    var processingTransport = builder.Configuration[
        "Auraly:Processing:Transport"] ?? "ServiceBus";
    if (string.Equals(processingTransport, "RabbitMq", StringComparison.OrdinalIgnoreCase))
    {
        var rabbitConnection = builder.Configuration[
            "Auraly:Processing:RabbitMq:ConnectionString"];
        var documentQueue = builder.Configuration[
            "Auraly:DocumentProcessing:RabbitMq:QueueName"];
        var fiscalQueue = builder.Configuration[
            "Auraly:Fiscal:RabbitMq:QueueName"];
        var accountingQueue = builder.Configuration[
            "Auraly:Accounting:RabbitMq:QueueName"];
        var externalCustomerQueue = builder.Configuration[
            "Auraly:ExternalCustomerReconciliation:QueueName"] ??
            "auraly-external-customer-reconciliation";
        if (string.IsNullOrWhiteSpace(rabbitConnection) ||
            string.IsNullOrWhiteSpace(documentQueue) ||
            string.IsNullOrWhiteSpace(fiscalQueue) ||
            string.IsNullOrWhiteSpace(accountingQueue) ||
            string.IsNullOrWhiteSpace(externalCustomerQueue))
            throw new InvalidOperationException(
                "RabbitMQ connection and document/fiscal/accounting/external-customer queue names are required.");

        builder.Services.AddSingleton(new RabbitMqProcessingOptions(
            rabbitConnection, documentQueue, fiscalQueue, accountingQueue));
        builder.Services.AddSingleton(
            new ExternalCustomerReconciliationRabbitMqOptions(externalCustomerQueue));
        builder.Services.AddSingleton<RabbitMqProcessingConnection>();
        builder.Services.AddSingleton<RabbitMqProcessingTransport>();
        builder.Services.AddSingleton<IDocumentProcessingSignalPublisher>(provider =>
            provider.GetRequiredService<RabbitMqProcessingTransport>());
        builder.Services.AddSingleton<IFiscalProcessingSignalPublisher>(provider =>
            provider.GetRequiredService<RabbitMqProcessingTransport>());
        builder.Services.AddSingleton<IAccountingProcessingSignalPublisher>(provider =>
            provider.GetRequiredService<RabbitMqProcessingTransport>());
        if (builder.Configuration.GetValue("Auraly:Fiscal:Worker:Enabled", true))
            builder.Services.AddHostedService<RabbitMqFiscalProcessingHostedService>();
        if (builder.Configuration.GetValue("Auraly:Accounting:Worker:Enabled", true))
            builder.Services.AddHostedService<RabbitMqAccountingProcessingHostedService>();
        if (builder.Configuration.GetValue(
                "Auraly:DocumentProcessing:Worker:Enabled", true))
            builder.Services.AddHostedService<RabbitMqDocumentProcessingHostedService>();
        if (builder.Configuration.GetValue(
                "Auraly:ExternalCustomerReconciliation:Worker:Enabled", true))
            builder.Services.AddHostedService<ExternalCustomerReconciliationRabbitMqHostedService>();
    }
    else if (string.Equals(
                 processingTransport, "ServiceBus", StringComparison.OrdinalIgnoreCase))
    {
        var queueName = builder.Configuration[
            "Auraly:DocumentProcessing:ServiceBus:QueueName"];
        if (string.IsNullOrWhiteSpace(queueName))
            throw new InvalidOperationException(
                "Auraly:DocumentProcessing:ServiceBus:QueueName is required.");

        builder.Services.AddSingleton(new DocumentProcessingServiceBusOptions(queueName));
        builder.Services.AddSingleton(
            AzureRuntimeClientFactory.CreateServiceBusClient(builder.Configuration));
        builder.Services.AddSingleton(provider =>
            provider.GetRequiredService<ServiceBusClient>().CreateSender(queueName));
        builder.Services.AddSingleton<IDocumentProcessingSignalPublisher,
            ServiceBusDocumentProcessingPublisher>();

        var fiscalQueueName = builder.Configuration[
            "Auraly:Fiscal:ServiceBus:QueueName"];
        if (string.IsNullOrWhiteSpace(fiscalQueueName))
            throw new InvalidOperationException(
                "Auraly:Fiscal:ServiceBus:QueueName is required. " +
                "Fiscal processing never falls back to SQL polling.");
        var accountingQueueName = builder.Configuration[
            "Auraly:Accounting:ServiceBus:QueueName"];
        if (string.IsNullOrWhiteSpace(accountingQueueName))
            throw new InvalidOperationException(
                "Auraly:Accounting:ServiceBus:QueueName is required. " +
                "Accounting processing never falls back to synchronous observers.");
        var externalCustomerQueueName = builder.Configuration[
            "Auraly:ExternalCustomerReconciliation:QueueName"] ??
            "auraly-external-customer-reconciliation";
        builder.Services.AddSingleton(
            new FiscalProcessingServiceBusOptions(fiscalQueueName));
        builder.Services.AddSingleton(
            new AccountingProcessingServiceBusOptions(accountingQueueName));
        builder.Services.AddSingleton(
            new ExternalCustomerReconciliationServiceBusOptions(
                externalCustomerQueueName));
        builder.Services.AddSingleton<IFiscalProcessingSignalPublisher,
            ServiceBusFiscalProcessingPublisher>();
        builder.Services.AddSingleton<IAccountingProcessingSignalPublisher,
            ServiceBusAccountingProcessingPublisher>();
        if (builder.Configuration.GetValue("Auraly:Fiscal:Worker:Enabled", true))
            builder.Services.AddHostedService<FiscalProcessingHostedService>();
        if (builder.Configuration.GetValue("Auraly:Accounting:Worker:Enabled", true))
            builder.Services.AddHostedService<AccountingProcessingHostedService>();
        if (builder.Configuration.GetValue(
                "Auraly:DocumentProcessing:Worker:Enabled", true))
            builder.Services.AddHostedService<DocumentProcessingHostedService>();
        builder.Services.AddHostedService<ExternalCustomerReconciliationServiceBusHostedService>();
    }
    else
    {
        throw new InvalidOperationException(
            "Auraly:Processing:Transport must be ServiceBus or RabbitMq.");
    }
}
var webPubSubConnection = builder.Configuration[
    "Auraly:PosSynchronization:WebPubSub:ConnectionString"];
var webPubSubEndpoint = builder.Configuration[
    "Auraly:PosSynchronization:WebPubSub:Endpoint"];
if (string.IsNullOrWhiteSpace(webPubSubConnection) &&
    string.IsNullOrWhiteSpace(webPubSubEndpoint))
    throw new InvalidOperationException(
        "Configure Auraly:PosSynchronization:WebPubSub:Endpoint for managed identity " +
        "or Auraly:PosSynchronization:WebPubSub:ConnectionString. " +
        "POS synchronization never falls back to polling.");
var webPubSubHub = builder.Configuration[
    "Auraly:PosSynchronization:WebPubSub:Hub"];
if (string.IsNullOrWhiteSpace(webPubSubHub)) webPubSubHub = "auraly_pos";
builder.Services.AddSingleton(_ =>
    AzureRuntimeClientFactory.CreateWebPubSubServiceClient(
        builder.Configuration, webPubSubConnection, webPubSubEndpoint, webPubSubHub));
builder.Services.AddSingleton<IPosSynchronizationPushGateway,
    AzureWebPubSubSynchronizationGateway>();
builder.Services.AddSingleton<SqlPosSynchronizationOutboxDispatcher>();
builder.Services.AddSingleton<IPosSynchronizationOutboxDispatcher>(provider =>
    provider.GetRequiredService<SqlPosSynchronizationOutboxDispatcher>());
if (builder.Configuration.GetValue(
        "Auraly:PosSynchronization:Worker:Enabled", true))
    builder.Services.AddHostedService<PosSynchronizationOutboxHostedService>();
builder.Services.AddScoped<ICatalogStore, SqlCatalogStore>();
builder.Services.AddScoped<IProductMerchandisingStore, SqlProductMerchandisingStore>();
builder.Services.AddScoped<ProductMerchandisingService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<PosCatalogService>();
builder.Services.AddScoped<IPartyStore, SqlPartyStore>();
builder.Services.AddScoped<IPartyWorkspaceStore, SqlPartyWorkspaceStore>();
builder.Services.AddScoped<ICommercialPartyRoleStore, SqlCommercialPartyRoleStore>();
builder.Services.AddScoped<IExternalCustomerReconciliationStore, SqlExternalCustomerReconciliationStore>();
builder.Services.AddScoped<ExternalCustomerReconciliationService>();
builder.Services.AddScoped<ExternalCustomerReconciliationSystemService>();
builder.Services.AddScoped<PartyWorkspaceService>();
builder.Services.AddScoped<CommercialPartyRoleService>();
builder.Services.AddScoped<PartyService>();
builder.Services.AddScoped<GeographyService>();
builder.Services.AddScoped<ISalesWorkspaceDirectory, SqlSalesWorkspaceDirectory>();
builder.Services.AddScoped<SalesWorkspaceService>();
builder.Services.AddScoped<IPosEnrollmentStore, SqlPosEnrollmentStore>();
builder.Services.AddScoped<PosEnrollmentService>();
builder.Services.AddScoped<IPosApprovalStore, SqlPosApprovalStore>();
builder.Services.AddScoped<PosApprovalService>();
builder.Services.AddScoped<IOnlineSalesDraftStore, SqlOnlineSalesDraftStore>();
builder.Services.AddScoped<OnlineSalesDraftService>();
builder.Services.AddScoped<IOnlineSalesCheckoutStore, SqlOnlineSalesDraftStore>();
builder.Services.AddScoped<OnlineSalesCheckoutService>();
builder.Services.AddScoped<IOnlineSalesHistoryStore, SqlOnlineSalesDraftStore>();
builder.Services.AddScoped<OnlineSalesHistoryService>();
builder.Services.AddScoped<IOnlineSalesOrderImportStore, SqlOnlineSalesDraftStore>();
builder.Services.AddScoped<OnlineSalesOrderImportService>();

builder.Services.AddScoped<IWorkSessionStore, SqlWorkSessionStore>();
builder.Services.AddScoped<WorkSessionService>();
builder.Services.Configure<AuthenticationJwtOptions>(
    builder.Configuration.GetSection(AuthenticationJwtOptions.SectionName));
builder.Services.AddSingleton<IAuthenticationTokenIssuer, JwtAuthenticationTokenIssuer>();
builder.Services.AddScoped<IAuthenticationPasswordVerifier, BcryptAuthenticationPasswordVerifier>();
builder.Services.AddScoped<IAuthenticationSessionStore, SqlAuthenticationSessionStore>();
builder.Services.AddScoped<Auraly.Application.Authentication.AuthenticationService>();
builder.Services.AddScoped<IAuthenticationSessionValidator>(
    services => services.GetRequiredService<Auraly.Application.Authentication.AuthenticationService>());
builder.Services.Configure<OfflineAuthenticationLeaseSigningOptions>(
    builder.Configuration.GetSection(OfflineAuthenticationLeaseSigningOptions.SectionName));
builder.Services.Configure<PosInstallerOptions>(
    builder.Configuration.GetSection(PosInstallerOptions.SectionName));
builder.Services.AddSingleton<RsaOfflineAuthenticationLeaseSigner>();
builder.Services.AddSingleton<IOfflineAuthenticationLeaseSigner>(services =>
    services.GetRequiredService<RsaOfflineAuthenticationLeaseSigner>());
builder.Services.AddSingleton<IOfflineAuthenticationLeaseTrustProvider>(services =>
    services.GetRequiredService<RsaOfflineAuthenticationLeaseSigner>());
builder.Services.AddScoped<IOfflineAuthenticationLeaseStore,
    SqlOfflineAuthenticationLeaseStore>();
builder.Services.AddSingleton(new OfflineAuthenticationLeasePolicy(
    TimeSpan.FromHours(builder.Configuration.GetValue(
        "Authentication:OfflineLeaseSigning:DurationHours", 8))));
builder.Services.AddScoped<OfflineAuthenticationLeaseService>();

builder.Services.AddScoped<IPosOfflineIdentityStore, SqlPosOfflineIdentityStore>();
builder.Services.AddScoped<PosOfflineIdentityService>();
builder.Services.AddScoped<IOrderStore, SqlOrderStore>();
builder.Services.AddScoped<IPosOrderActorResolver, SqlPosOrderActorResolver>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OrderRecoveryService>();
builder.Services.AddScoped<IOrderBatchStore, SqlOrderBatchStore>();
builder.Services.AddScoped<OrderBatchService>();
builder.Services.AddScoped<IGoodsReceiptStore, SqlGoodsReceiptStore>();
builder.Services.AddScoped<IGoodsReceiptWorkspaceStore, SqlGoodsReceiptWorkspaceStore>();
builder.Services.AddScoped<GoodsReceiptWorkspaceService>();
builder.Services.AddScoped<GoodsReceiptService>();
builder.Services.AddScoped<IPurchaseReturnStore, SqlPurchaseReturnStore>();
builder.Services.AddScoped<PurchaseReturnService>();
builder.Services.AddScoped<IPayablesStore, SqlPayablesStore>();
builder.Services.AddScoped<PayablesService>();
builder.Services.AddScoped<IReceivablesStore, SqlReceivablesStore>();
builder.Services.AddScoped<ReceivablesService>();
builder.Services.AddScoped<IPricingStore, SqlPricingStore>();
builder.Services.AddScoped<PricingService>();
builder.Services.AddScoped<IInventoryOperationStore, SqlInventoryOperationStore>();
builder.Services.AddScoped<IInventoryQueryStore, SqlInventoryQueryStore>();
builder.Services.AddScoped<InventoryQueryService>();
builder.Services.AddScoped<InventoryOperationService>();
builder.Services.AddScoped<IRouteStore, SqlRouteStore>();
builder.Services.AddScoped<RouteService>();
builder.Services.AddScoped<IDispatchStore, SqlDispatchStore>();
builder.Services.AddScoped<DispatchService>();
builder.Services.AddScoped<ISalesReturnStore, SqlSalesReturnStore>();
builder.Services.AddScoped<SalesReturnService>();
builder.Services.AddScoped<ISalesReturnQueryStore, SqlSalesReturnQueryStore>();
builder.Services.AddScoped<SalesReturnQueryService>();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.AddSingleton(new TenantInvitationEmailOptions(
    builder.Configuration["Auraly:Email:ConnectionString"],
    builder.Configuration["Auraly:Email:SenderAddress"] ?? "DoNotReply@auralyapp.co",
    builder.Configuration["Auraly:Email:PublicAppUrl"] ?? "https://auralyapp.co",
    builder.Configuration["Auraly:Email:LogoUrl"] ?? "https://auralyapp.co/brand/auraly-mark.png",
    builder.Configuration["Auraly:Email:SupportEmail"] ?? "soporte@auralyapp.co"));
builder.Services.AddHostedService<TenantInvitationEmailHostedService>();


var jwtIssuer = builder.Configuration["Authentication:Jwt:Issuer"];
var jwtAudience = builder.Configuration["Authentication:Jwt:Audience"];
var jwtSigningKey = builder.Configuration["Authentication:Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtSigningKey) || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
    throw new InvalidOperationException(
        "Authentication JWT issuer, audience and a signing key of at least 32 bytes are required.");
var validationKey = Encoding.UTF8.GetBytes(jwtSigningKey);
builder.Services.AddScoped<AuthenticationSessionJwtBearerEvents>();

const string adaptiveAuthenticationScheme = "Auraly.Adaptive";
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = adaptiveAuthenticationScheme;
        options.DefaultAuthenticateScheme = adaptiveAuthenticationScheme;
        options.DefaultChallengeScheme = adaptiveAuthenticationScheme;
    })
    .AddPolicyScheme(adaptiveAuthenticationScheme, adaptiveAuthenticationScheme, options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey("X-Auraly-Device-Id")
                ? PosAuthenticationDefaults.Scheme
                : JwtBearerDefaults.AuthenticationScheme;
    })
    .AddScheme<AuthenticationSchemeOptions, PosDeviceAuthenticationHandler>(
        PosAuthenticationDefaults.Scheme,
        _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
            ValidIssuer = jwtIssuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(validationKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "sub"
        };
        options.EventsType = typeof(AuthenticationSessionJwtBearerEvents);
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authentication.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("fiscal.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("catalog.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("parties.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("pos.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("orders.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("purchasing.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("payables.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("returns.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("receivables.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("pricing.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("inventory.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("routes.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("accounting.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("pos.catalog.sync", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(PosAuthenticationDefaults.PermissionClaim, CatalogPermissionCodes.Sync);
    });
    options.AddPolicy("pos.fiscal.status.sync", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(PosAuthenticationDefaults.PermissionClaim,
            FiscalPermissionCodes.PosStatusSync);
    });
    options.AddPolicy("pos.customer.create", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            PosAuthenticationDefaults.PermissionClaim,
            PartyPermissionCodes.PosCustomerCreate);
    });
    options.AddPolicy("pos.identity.sync", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            PosAuthenticationDefaults.PermissionClaim,
            CommercePermissionCodes.PosIdentitySync);
    });
    options.AddPolicy("pos.offline.authentication", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            PosAuthenticationDefaults.PermissionClaim,
            CommercePermissionCodes.PosIdentitySync);
    });
    options.AddPolicy("pos.synchronization", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            PosAuthenticationDefaults.PermissionClaim,
            CatalogPermissionCodes.Sync);
    });
    options.AddPolicy("pos.orders", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            PosAuthenticationDefaults.PermissionClaim,
            CommercePermissionCodes.PosIdentitySync);
    });
    options.AddPolicy("pos.approvals.consume", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            PosAuthenticationDefaults.PermissionClaim,
            CommercePermissionCodes.SalesCreate);
    });
    options.AddPolicy(
        "pos.sales.upload",
        policy =>
        {
            policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(
                PosAuthenticationDefaults.PermissionClaim,
                CommercePermissionCodes.SalesCreate);
        });
    options.AddPolicy("pos.cash.manage", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            PosAuthenticationDefaults.PermissionClaim,
            WorkSessionPermissionCodes.ManageCash);
    });
});
var app = builder.Build();
app.UseResponseCompression();
app.UseAuralyPlatformBeforeAuthentication();
app.UseAuthentication();
app.UseAuralyExecutionContext();
app.UseAuthorization();
app.UseAuralyPlatformAudit();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapControllers();
app.MapAuthenticationApi();
app.MapExecutionContextApi();
app.MapCatalogApi();
app.MapProductMerchandisingApi();
app.MapPartyApi();
app.MapPartyWorkspaceApi();
app.MapExternalCustomerReconciliationApi();
app.MapPartyUserAccountApi();
app.MapSalesWorkspaceApi();
app.MapPosEnrollmentApi();
app.MapPosInstallerApi();
app.MapPosApprovalApi();
app.MapOnlineSalesDraftApi();

app.MapWorkSessionApi();
app.MapPosIdentityApi();
app.MapOfflineAuthenticationLeaseApi();
app.MapPosSynchronizationApi();
app.MapFiscalApi();
app.MapFiscalConfigurationApi();
app.MapOrdersApi();
app.MapPosOrdersApi();
app.MapPurchasingApi();
app.MapReceivablesApi();
app.MapPayablesApi();
app.MapReturnsApi();
app.MapSalesReturnQueryApi();
app.MapInventoryApi();
app.MapRoutesApi();
app.MapDispatchingApi();
app.MapPricingApi();
app.MapPriceSegmentsApi();
app.MapAccountingApi();
app.MapTaxationApi();
app.MapPost(
        "/api/pos/v1/sales",
        async (
            HttpContext httpContext,
            PosSaleUploadRequest request,
            ReceivePosSaleService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
                var response = await service.ReceiveAsync(
                    httpContext.User.ToPosDeviceIdentity(),
                    idempotencyKey,
                    request,
                    cancellationToken);
                return response.Status == PosSaleRemoteStatuses.FiscalIntegrityConflict
                    ? Results.Conflict(response)
                    : Results.Ok(response);
            }
            catch (PosSaleForbiddenException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (PosSaleInvalidException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (PosSaleIdempotencyConflictException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "IdempotencyConflict");
            }
            catch (PosSaleProcessingBusyException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "DocumentProcessingBusy");
            }
        })
    .RequireAuthorization("pos.sales.upload");

await app.SeedAuralyPlatformPermissionsAsync();
app.Run();

public partial class Program;
