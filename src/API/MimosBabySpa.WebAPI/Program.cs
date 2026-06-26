using System.Text;
using Azure;
using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MimosBabySpa.Application.Auth.Interfaces;
using MimosBabySpa.Application.Auth.Services;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Facts.Resolvers;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Testing;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Billing;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Application.Identity.Services;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Catalog;
using MimosBabySpa.Infrastructure.Commerce;
using MimosBabySpa.Infrastructure.Configuration;
using MimosBabySpa.Infrastructure.LLM;
using MimosBabySpa.Infrastructure.CrossCutting;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Identity;
using MimosBabySpa.Infrastructure.MultiTenancy;
using MimosBabySpa.Infrastructure.Repositories;
using MimosBabySpa.Infrastructure.Services;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Configuration;
using MimosBabySpa.WebAPI.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICorrelationIdProvider, CorrelationIdProvider>();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<DemoRequestOptions>(builder.Configuration.GetSection(DemoRequestOptions.SectionName));
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();

builder.Services.Configure<GoogleAuthSettings>(builder.Configuration.GetSection(GoogleAuthSettings.SectionName));
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<ILeadRepository, LeadRepository>();
builder.Services.AddScoped<IReservationRepository, ReservationRepository>();
builder.Services.AddScoped<IConversationStateRepository, ConversationStateRepository>();
builder.Services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();
builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUsageBillingService, UsageBillingService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IOutboundMessageDispatcher, OutboundMessageDispatcher>();
builder.Services.AddScoped<IInboundMessageDeduplicationService, InboundMessageDeduplicationService>();
builder.Services.AddSingleton<IWhatsAppInboundQueueService, WhatsAppInboundQueueService>();
builder.Services.AddScoped<IWorkingHoursService, WorkingHoursService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IConversationLifecycleService, ConversationLifecycleService>();
builder.Services.AddScoped<IConversationStateManager, ConversationStateManager>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();
builder.Services.AddScoped<ServiceNameResolver>();
builder.Services.AddScoped<ReservationPricingResolver>();
builder.Services.AddScoped<IPromotionPricingService, PromotionPricingService>();
builder.Services.AddScoped<IBusinessClock, BusinessClock>();
builder.Services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();
builder.Services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();
builder.Services.AddScoped<ICommerceService, CommerceService>();
builder.Services.AddScoped<ICommerceAdapter, LocalCommerceAdapter>();
builder.Services.AddHttpClient<SiigoCommerceAdapter>();
builder.Services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<SiigoCommerceAdapter>());
builder.Services.AddScoped<ICommerceAdapterFactory, CommerceAdapterFactory>();
builder.Services.AddScoped<IAddOnCatalogService, AddOnCatalogService>();
builder.Services.AddScoped<IIntegrationsConfigProvider, IntegrationsConfigProvider>();
builder.Services.AddScoped<ISchedulingPolicyProvider, SchedulingPolicyProvider>();
builder.Services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();
builder.Services.AddScoped<IPaymentConfirmationHandler, PaymentConfirmationHandler>();
builder.Services.AddScoped<IPaidCheckoutFulfillmentRegistry, PaidCheckoutFulfillmentRegistry>();
builder.Services.AddScoped<IPaidCheckoutFulfillmentHandler, ReservationPaidCheckoutFulfillmentHandler>();
builder.Services.AddScoped<IPaidCheckoutFulfillmentHandler, EnrollmentPaidCheckoutFulfillmentHandler>();
builder.Services.AddScoped<IPaidCheckoutFulfillmentHandler, OrderPaidCheckoutFulfillmentHandler>();
builder.Services.AddScoped<IMediaUrlResolver, BlobMediaUrlResolver>();
builder.Services.AddScoped<IMessageSequenceResolver, MessageSequenceResolver>();
builder.Services.AddScoped<IActiveAgentConfigResolver, ActiveAgentConfigResolver>();
builder.Services.AddScoped<IEventNotificationDispatcher, EventNotificationDispatcher>();
builder.Services.AddScoped<IBusinessInboundContactRouter, BusinessInboundContactRouter>();
builder.Services.AddScoped<IExternalEscalationService, ExternalEscalationService>();
builder.Services.AddScoped<IExternalEscalationTargetHandler, OrderDeliveryExternalEscalationHandler>();
builder.Services.AddScoped<IConversationFactsService, ConversationFactsService>();
builder.Services.AddScoped<ICustomerMemoryService, CustomerMemoryService>();
builder.Services.AddScoped<IRequestContextService, RequestContextService>();
builder.Services.AddScoped<IReservationLifecycleService, ReservationLifecycleService>();
builder.Services.AddScoped<ICustomerReservationResolver, CustomerReservationResolver>();
builder.Services.AddScoped<IPaymentLifecycleService, PaymentLifecycleService>();
builder.Services.AddScoped<IReservationIntentBuilder, ReservationIntentBuilder>();
builder.Services.AddScoped<ICheckoutQuoteService, CheckoutQuoteService>();
builder.Services.AddScoped<ICheckoutPaymentCoordinator, CheckoutPaymentCoordinator>();
builder.Services.AddScoped<IEscalationNotifier, EscalationNotifier>();
builder.Services.AddScoped<IEscalationConfigProvider, EscalationConfigProvider>();
builder.Services.Configure<ReleaseLinkSettings>(builder.Configuration.GetSection(ReleaseLinkSettings.SectionName));
builder.Services.AddScoped<IReleaseLinkService, ReleaseLinkService>();
builder.Services.AddHttpClient<GoogleCalendarService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<ICalendarService, GoogleCalendarService>();
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("AzureStorage")
        ?? configuration["AzureStorage:ConnectionString"]
        ?? configuration["AzureWebJobsStorage"];

    if (string.IsNullOrWhiteSpace(connectionString))
        connectionString = "UseDevelopmentStorage=true";

    return new BlobServiceClient(connectionString);
});

builder.Services.AddHttpClient();
builder.Services.Configure<WhatsAppWebhookOptions>(builder.Configuration.GetSection(WhatsAppWebhookOptions.SectionName));
builder.Services.AddScoped<IWhatsAppCredentialResolver, WhatsAppCredentialResolver>();
builder.Services.AddScoped<IWhatsAppService>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var resolver = sp.GetRequiredService<IWhatsAppCredentialResolver>();
    var logger = sp.GetRequiredService<ILogger<WhatsAppService>>();
    var webhookOptions = sp.GetRequiredService<IOptions<WhatsAppWebhookOptions>>();
    return new WhatsAppService(httpClient, resolver, logger, webhookOptions);
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IBusinessAdminService, BusinessAdminService>();
builder.Services.AddScoped<IServiceAdminService, ServiceAdminService>();
builder.Services.AddScoped<IProductAdminService, ProductAdminService>();
builder.Services.AddScoped<IPromotionAdminService, PromotionAdminService>();
builder.Services.AddScoped<IEmployeeAdminService, EmployeeAdminService>();
builder.Services.AddScoped<IReservationAdminService, ReservationAdminService>();
builder.Services.AddScoped<ILeadAdminService, LeadAdminService>();
builder.Services.AddScoped<IIntegrationAdminService, IntegrationAdminService>();
builder.Services.AddScoped<IWorkingHoursAdminService, WorkingHoursAdminService>();
builder.Services.AddScoped<IAgentRepository, AgentRepository>();
builder.Services.AddScoped<IAgentAdminService, AgentAdminService>();
builder.Services.AddScoped<ICatalogImportAdminService, CatalogImportAdminService>();
builder.Services.AddScoped<ICatalogDocumentTextExtractor, CatalogDocumentTextExtractor>();
builder.Services.AddScoped<ICatalogDraftParser, CatalogDraftAiParser>();

builder.Services.Configure<OpenAITextModelOptions>(builder.Configuration.GetSection(OpenAITextModelOptions.SectionName));
builder.Services.AddSingleton<OpenAIClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAITextModelOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.Endpoint) || string.IsNullOrWhiteSpace(options.ApiKey))
        throw new InvalidOperationException("OpenAI:TextModel:Endpoint y ApiKey deben estar configurados en WebAPI.");
    return new OpenAIClient(new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey));
});
builder.Services.AddScoped<IChatClient>(sp =>
{
    var client = sp.GetRequiredService<OpenAIClient>();
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAITextModelOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<AzureOpenAIChatClient>>();
    return new AzureOpenAIChatClient(client, options.DeploymentName, logger);
});
builder.Services.AddScoped<IAgentConfigProvider, AgentConfigProvider>();
builder.Services.AddSingleton<IFactSourceResolver, ChannelPhoneResolver>();
builder.Services.AddSingleton<IFactSourceResolver, ChannelEmailResolver>();
builder.Services.AddSingleton<IFactSourceResolver, EngagementResolver>();
builder.Services.AddSingleton<IFactHydrator, FactHydrator>();
builder.Services.AddSingleton<IFlowStageDetector, FlowStageDetector>();
builder.Services.AddScoped<IConversationVerificationService, ConversationVerificationService>();
builder.Services.AddScoped<IGuardEvaluator, GuardEvaluator>();
builder.Services.AddScoped<IToolCapabilityGate, ToolCapabilityGate>();
builder.Services.AddScoped<IOperatingHoursTurnPolicy, OperatingHoursTurnPolicy>();
builder.Services.AddScoped<IAgentTurnToolResolver, AgentTurnToolResolver>();
builder.Services.AddScoped<IPromptComposer, AgentPromptComposer>();
builder.Services.AddScoped<IAgentTemplateResolver, AgentTemplateResolver>();
builder.Services.AddScoped<ITemplateRenderer, PromptTemplateRenderer>();
builder.Services.AddScoped<IAgentTurnResponseComposer, AgentTurnResponseComposer>();
builder.Services.AddScoped<IAgentTool, CheckAvailabilityTool>();
builder.Services.AddScoped<IAgentTool, ResolvePricingTool>();
builder.Services.AddScoped<IAgentTool, PrepareCheckoutTool>();
builder.Services.AddScoped<IAgentTool, PrepareOrderCheckoutTool>();
builder.Services.AddScoped<IAgentTool, CreateReservationTool>();
builder.Services.AddScoped<IAgentTool, AssignPaidSlotTool>();
builder.Services.AddScoped<IAgentTool, SuspendReservationTool>();
builder.Services.AddScoped<IAgentTool, GetCustomerReservationsTool>();
builder.Services.AddScoped<IAgentTool, ConfirmReservationAttendanceTool>();
builder.Services.AddScoped<IAgentTool, PrepareReservationChangeTool>();
builder.Services.AddScoped<IAgentTool, ConfirmReservationChangeTool>();
builder.Services.AddScoped<IAgentTool, VerifyPaymentTool>();
builder.Services.AddScoped<IAgentTool, EscalateToHumanTool>();
builder.Services.AddScoped<IAgentTool, GetServiceCatalogTool>();
builder.Services.AddScoped<IAgentTool, GetCompatibleAddOnsTool>();
builder.Services.AddScoped<IAgentTool, GetServiceFulfillmentTool>();
builder.Services.AddScoped<IAgentTool, SetFactTool>();
builder.Services.AddScoped<IAgentTool, ResetFlowContextTool>();
builder.Services.AddScoped<IAgentTool, SendMessageSequenceTool>();
builder.Services.AddScoped<IAgentTool, SearchProductsTool>();
builder.Services.AddScoped<IAgentTool, AddOrderItemTool>();
builder.Services.AddScoped<IAgentTool, RemoveOrderItemTool>();
builder.Services.AddScoped<IAgentTool, UpdateOrderItemQuantityTool>();
builder.Services.AddScoped<IAgentTool, GetOrderDraftTool>();
builder.Services.AddScoped<IAgentTool, CreateOrderTool>();
builder.Services.AddScoped<IAgentTool, StartExternalInteractionTool>();
builder.Services.AddScoped<IAgentTool, ResolveExternalInteractionTool>();
builder.Services.AddScoped<IAgentTool, SearchOrderTool>();
builder.Services.AddScoped<IAgentTool, AcceptOrderDeliveryTool>();
builder.Services.AddScoped<IAgentTool, RejectOrderDeliveryTool>();
builder.Services.AddScoped<IAgentTool, CompleteExternalInteractionTool>();
builder.Services.AddScoped<IAgentTool, OperationsGetReservationsTool>();
builder.Services.AddScoped<IAgentTool, OperationsBlockAvailabilityTool>();
builder.Services.AddScoped<IAgentTool, OperationsRequestRescheduleTool>();
builder.Services.AddScoped<IAgentTool, OperationsBusinessMetricsTool>();
builder.Services.AddScoped<IAgentTool, OperationsCustomerHistoryTool>();
builder.Services.AddScoped<AgentToolRegistry>();
builder.Services.AddScoped<IAgentConversationService, AgentConversationService>();
builder.Services.AddScoped<IAgentTestRuntimeFactory, AgentTestRuntimeFactory>();

builder.Services.AddScoped<IConversationAdminService, ConversationAdminService>();
builder.Services.AddScoped<IOrderAdminService, OrderAdminService>();
builder.Services.AddScoped<IPaymentAdminService, PaymentAdminService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MimosBabySpa Admin API",
        Version = "v1",
        Description = "API de administración web multitenant"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebApp", policy =>
        policy
            .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .WithExposedHeaders("X-Correlation-Id"));
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MimosBabySpa API v1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseCors("WebApp");

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<AuditLogMiddleware>();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
    await permissionService.SeedPermissionsAsync();
}

app.Run();
