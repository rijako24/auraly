using Auraly.Api;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.WebPubSub;
using System.Text;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Application.Authentication;
using Auraly.Application.Authorization;
using Auraly.Application.Catalog;
using Auraly.Application.Cash;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Parties;
using Auraly.Application.Organization;
using Auraly.Application.Orders;
using Auraly.Application.WorkSessions;
using Auraly.Application.Purchasing;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Parties;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Fiscal;
using Auraly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Auraly");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Auraly must point to the SQL Server database owned by Auraly.Database.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
builder.Services.AddSingleton(new SqlServerConnectionFactory(connectionString));
builder.Services.AddSingleton<IFiscalTechnicalKeyProvider, ConfigurationFiscalTechnicalKeyProvider>();
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
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlPosSaleDocumentHandler>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlGoodsReceiptDocumentHandler>();
builder.Services.AddScoped<DocumentProcessingEngine>();
builder.Services.AddScoped<DocumentProcessingWorker>();
builder.Services.AddSingleton<FiscalProcessingCoordinator>();
builder.Services.AddScoped<ReceivePosSaleService>();
if (!builder.Environment.IsEnvironment("Testing"))
{
    var serviceBusConnection = builder.Configuration[
        "Auraly:DocumentProcessing:ServiceBus:ConnectionString"];
    if (string.IsNullOrWhiteSpace(serviceBusConnection))
        throw new InvalidOperationException(
            "Auraly:DocumentProcessing:ServiceBus:ConnectionString is required. " +
            "Auraly never falls back to an in-memory processing transport.");
    var queueName = builder.Configuration[
        "Auraly:DocumentProcessing:ServiceBus:QueueName"];
    if (string.IsNullOrWhiteSpace(queueName))
        throw new InvalidOperationException(
            "Auraly:DocumentProcessing:ServiceBus:QueueName is required.");

    builder.Services.AddSingleton(new DocumentProcessingServiceBusOptions(queueName));
    builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnection));
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<ServiceBusClient>().CreateSender(queueName));
    builder.Services.AddSingleton<IDocumentProcessingSignalPublisher,
        ServiceBusDocumentProcessingPublisher>();

    var fiscalQueueName = builder.Configuration[
        "Auraly:Fiscal:ServiceBus:QueueName"];
    if (string.IsNullOrWhiteSpace(fiscalQueueName))
        throw new InvalidOperationException(
            "Auraly:Fiscal:ServiceBus:QueueName is required. " +
            "Fiscal processing never falls back to SQL polling.");
    builder.Services.AddSingleton(
        new FiscalProcessingServiceBusOptions(fiscalQueueName));
    builder.Services.AddSingleton<IFiscalProcessingSignalPublisher,
        ServiceBusFiscalProcessingPublisher>();
    if (builder.Configuration.GetValue("Auraly:Fiscal:Worker:Enabled", true))
        builder.Services.AddHostedService<FiscalProcessingHostedService>();

    if (builder.Configuration.GetValue(
            "Auraly:DocumentProcessing:Worker:Enabled", true))
        builder.Services.AddHostedService<DocumentProcessingHostedService>();
}
var webPubSubConnection = builder.Configuration[
    "Auraly:PosSynchronization:WebPubSub:ConnectionString"];
if (string.IsNullOrWhiteSpace(webPubSubConnection))
    throw new InvalidOperationException(
        "Auraly:PosSynchronization:WebPubSub:ConnectionString is required. " +
        "POS synchronization never falls back to polling.");
var webPubSubHub = builder.Configuration[
    "Auraly:PosSynchronization:WebPubSub:Hub"];
if (string.IsNullOrWhiteSpace(webPubSubHub)) webPubSubHub = "auraly_pos";
builder.Services.AddSingleton(
    new WebPubSubServiceClient(webPubSubConnection, webPubSubHub));
builder.Services.AddSingleton<IPosSynchronizationPushGateway,
    AzureWebPubSubSynchronizationGateway>();
builder.Services.AddSingleton<SqlPosSynchronizationOutboxDispatcher>();
builder.Services.AddSingleton<IPosSynchronizationOutboxDispatcher>(provider =>
    provider.GetRequiredService<SqlPosSynchronizationOutboxDispatcher>());
builder.Services.AddHostedService<PosSynchronizationOutboxHostedService>();
builder.Services.AddScoped<ICatalogStore, SqlCatalogStore>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<PosCatalogService>();
builder.Services.AddScoped<IPartyStore, SqlPartyStore>();
builder.Services.AddScoped<PartyService>();
builder.Services.AddScoped<GeographyService>();
builder.Services.AddScoped<IOnlineRegisterDirectory, SqlOnlineRegisterDirectory>();
builder.Services.AddScoped<OnlineRegisterService>();
builder.Services.AddScoped<IPosEnrollmentStore, SqlPosEnrollmentStore>();
builder.Services.AddScoped<PosEnrollmentService>();
builder.Services.AddScoped<IOnlineSalesDraftStore, SqlOnlineSalesDraftStore>();
builder.Services.AddScoped<OnlineSalesDraftService>();
builder.Services.AddScoped<IOnlineSalesCheckoutStore, SqlOnlineSalesDraftStore>();
builder.Services.AddScoped<OnlineSalesCheckoutService>();
builder.Services.AddScoped<IOnlineSalesHistoryStore, SqlOnlineSalesDraftStore>();
builder.Services.AddScoped<OnlineSalesHistoryService>();
builder.Services.AddScoped<IOnlineSalesOrderImportStore, SqlOnlineSalesDraftStore>();
builder.Services.AddScoped<OnlineSalesOrderImportService>();
builder.Services.AddScoped<ICashSessionStore, SqlCashSessionStore>();
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
builder.Services.AddScoped<CashSessionService>();
builder.Services.AddScoped<IPosOfflineIdentityStore, SqlPosOfflineIdentityStore>();
builder.Services.AddScoped<PosOfflineIdentityService>();
builder.Services.AddScoped<IOrderStore, SqlOrderStore>();
builder.Services.AddScoped<IPosOrderActorResolver, SqlPosOrderActorResolver>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OrderRecoveryService>();
builder.Services.AddScoped<IOrderBatchStore, SqlOrderBatchStore>();
builder.Services.AddScoped<OrderBatchService>();
builder.Services.AddScoped<IGoodsReceiptStore, SqlGoodsReceiptStore>();
builder.Services.AddScoped<GoodsReceiptService>();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

var jwtIssuer = builder.Configuration["Authentication:Jwt:Issuer"];
var jwtAudience = builder.Configuration["Authentication:Jwt:Audience"];
var jwtSigningKey = builder.Configuration["Authentication:Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtSigningKey) || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
    throw new InvalidOperationException(
        "Authentication JWT issuer, audience and a signing key of at least 32 bytes are required.");
var validationKey = Encoding.UTF8.GetBytes(jwtSigningKey);
builder.Services.AddScoped<AuthenticationSessionJwtBearerEvents>();

builder.Services
    .AddAuthentication(PosAuthenticationDefaults.Scheme)
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
});
var app = builder.Build();
app.UseResponseCompression();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapAuthenticationApi();
app.MapCatalogApi();
app.MapPartyApi();
app.MapPartyUserAccountApi();
app.MapOnlineRegisterApi();
app.MapPosEnrollmentApi();
app.MapOnlineSalesDraftApi();
app.MapCashApi();
app.MapWorkSessionApi();
app.MapPosIdentityApi();
app.MapOfflineAuthenticationLeaseApi();
app.MapPosSynchronizationApi();
app.MapFiscalApi();
app.MapOrdersApi();
app.MapPosOrdersApi();
app.MapPurchasingApi();
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

app.Run();

public partial class Program;

