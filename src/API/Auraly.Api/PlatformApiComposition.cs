using System.Text;
using Auraly.Application.Authentication;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Infrastructure.Persistence;


using Azure;

using Azure.AI.OpenAI;

using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Authentication.JwtBearer;

using Microsoft.AspNetCore.Authorization;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Options;

using Microsoft.IdentityModel.Tokens;

using Microsoft.OpenApi.Models;

using Auraly.Platform.Application.Auth.Interfaces;

using Auraly.Platform.Application.Auth.Services;

using Auraly.Platform.Application.Agents;

using Auraly.Platform.Application.Agents.Composition;

using Auraly.Platform.Application.Agents.Facts;

using Auraly.Platform.Application.Agents.Facts.Resolvers;

using Auraly.Platform.Application.Agents.Gating;

using Auraly.Platform.Application.Agents.Runtime;

using Auraly.Platform.Application.Agents.Templates;

using Auraly.Platform.Application.Agents.Testing;

using Auraly.Platform.Application.Agents.Operations.Support;

using Auraly.Platform.Application.Billing;

using Auraly.Platform.Application.Campaigns.Interfaces;

using Auraly.Platform.Application.Campaigns.Services;

using Auraly.Platform.Application.BusinessRules;

using Auraly.Platform.Application.Common.Interfaces;

using Auraly.Platform.Application.Commerce;

using Auraly.Platform.Application.Configuration;

using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;

using Auraly.Platform.Application.Identity.Services;

using Auraly.Platform.Application.LLM;

using Auraly.Platform.Application.Services;

using Auraly.Platform.Application.Promotions;

using Auraly.Platform.Application.StateManagement;

using Auraly.Platform.Application.Time;

using Auraly.Platform.Application.WhatsAppTemplates.Interfaces;

using Auraly.Platform.Domain.Repositories;

using Auraly.Platform.Infrastructure.Catalog;

using Auraly.Platform.Infrastructure.Commerce;

using Auraly.Platform.Infrastructure.Configuration;

using Auraly.Platform.Infrastructure.LLM;

using Auraly.Platform.Infrastructure.CrossCutting;

using Auraly.Platform.Infrastructure.Data;

using Auraly.Platform.Infrastructure.Identity;

using Auraly.Platform.Infrastructure.MultiTenancy;

using Auraly.Platform.Infrastructure.Repositories;

using Auraly.Platform.Infrastructure.Services;

using Auraly.Api.Authorization;

using Auraly.Api.Configuration;

using Auraly.Api.Middleware;

namespace Auraly.Api;

public static class PlatformApiComposition
{
    public static void AddAuralyPlatformConfiguration(this WebApplicationBuilder builder)
    {
        var endpoint = builder.Configuration["AppConfiguration:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        builder.Configuration.AddAzureAppConfiguration(options =>
            options.Connect(
                new Uri(endpoint),
                AzureManagedClientFactory.CreateCredential(
                    builder.Configuration, "AppConfiguration:ManagedIdentityClientId")));

        // Deployment-specific secrets and emergency overrides must win over
        // centrally shared settings, including stale or empty remote values.
        builder.Configuration.AddEnvironmentVariables();
    }

    public static void AddPlatformApi(
        this WebApplicationBuilder builder,
        bool configureAuthentication = true)
    {
        var connectionString =
            builder.Configuration.GetConnectionString("Auraly")
            ?? builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Auraly or ConnectionStrings:DefaultConnection is required.");
        }

        builder.Services.AddSingleton(new AuralySqlConnectionSource(connectionString));
        builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
            options.UseSqlServer(
                services.GetRequiredService<AuralySqlConnectionSource>().ConnectionString));

        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<ICorrelationIdProvider, CorrelationIdProvider>();

        builder.Services.AddScoped<ITenantContext, TenantContext>();

        builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
        var jwtIssuer = builder.Configuration["Authentication:Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Authentication:Jwt:Audience"];
        var jwtSigningKey = builder.Configuration["Authentication:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) ||
            string.IsNullOrWhiteSpace(jwtSigningKey) || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
        {
            throw new InvalidOperationException(
                "The canonical authentication issuer, audience and signing key are required.");
        }
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
        builder.Services.AddSingleton<SqlServerConnectionFactory>();
        builder.Services.AddScoped<IAuthenticationSessionStore, SqlAuthenticationSessionStore>();
        builder.Services.AddScoped<IAuthenticationSessionValidator, AuthenticationSessionValidator>();
        builder.Services.AddScoped<AuthenticationSessionJwtBearerEvents>();


        builder.Services.Configure<DemoRequestOptions>(builder.Configuration.GetSection(DemoRequestOptions.SectionName));


        if (configureAuthentication)
                {
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

                            ValidIssuer = jwtIssuer,

                            ValidateAudience = true,

                            ValidAudience = jwtAudience,

                            ValidateIssuerSigningKey = true,

                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),

                            ValidateLifetime = true,


                            ClockSkew = TimeSpan.Zero

                        };
                        options.EventsType = typeof(AuthenticationSessionJwtBearerEvents);
                    });
                }

                builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

        builder.Services.AddAuthorization();



        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        builder.Services.AddScoped<IConversationRepository, ConversationRepository>();

        builder.Services.AddScoped<IMessageRepository, MessageRepository>();

        builder.Services.AddScoped<ILeadRepository, LeadRepository>();

        builder.Services.AddScoped<ICampaignRepository, CampaignRepository>();

        builder.Services.AddScoped<IReservationRepository, ReservationRepository>();

        builder.Services.AddScoped<IConversationStateRepository, ConversationStateRepository>();

        builder.Services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();

        builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();

        builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();


        builder.Services.AddScoped<IAuthService, AuthService>();

        builder.Services.AddScoped<IUsageBillingService, UsageBillingService>();

        builder.Services.AddScoped<IMessageService, MessageService>();

        builder.Services.AddScoped<IOutboundMessageDispatcher, OutboundMessageDispatcher>();
        builder.Services.AddScoped<IConversationFollowUpService, ConversationFollowUpService>();

        builder.Services.AddScoped<IInboundMessageDeduplicationService, InboundMessageDeduplicationService>();

        builder.Services.AddScoped<IConversationInboundService, ConversationInboundService>();

        builder.Services.AddSingleton<IWhatsAppInboundQueueService, WhatsAppInboundQueueService>();

        builder.Services.AddSingleton<ICampaignQueueService, CampaignQueueService>();

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

        builder.Services.AddScoped<ServiceSelectionResolver>();

        builder.Services.AddScoped<ReservationPricingResolver>();

        builder.Services.AddScoped<IPromotionPricingService, PromotionPricingService>();

        builder.Services.AddScoped<IServiceCatalogPricingService, ServiceCatalogPricingService>();

        builder.Services.AddScoped<IBusinessClock, BusinessClock>();

        builder.Services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();

        builder.Services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();

        builder.Services.AddScoped<IProductCatalogAvailabilityService, ProductCatalogAvailabilityService>();

        builder.Services.AddScoped<ICommerceService, CommerceService>();
        builder.Services.AddScoped<ICommerceOrderWorkspaceResolver, Auraly.Platform.Infrastructure.Commerce.CommerceOrderWorkspaceResolver>();
        builder.Services.AddScoped<ICommerceCustomerResolver, CommerceCustomerResolver>();
        builder.Services.AddScoped<ICanonicalCommerceCustomerLookup, CanonicalCommerceCustomerLookup>();

        builder.Services.AddScoped<IProductLookupService>(provider => (IProductLookupService)provider.GetRequiredService<ICommerceService>());

        builder.Services.AddScoped<ICatalogRecommendationService, CatalogRecommendationService>();

        builder.Services.AddScoped<ICommerceAdapter, LocalCommerceAdapter>();

        builder.Services.AddHttpClient<SiigoCommerceAdapter>();

        builder.Services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<SiigoCommerceAdapter>());

        builder.Services.AddHttpClient<MantisCommerceAdapter>();

        builder.Services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<MantisCommerceAdapter>());

        builder.Services.AddHttpClient<XionCommerceAdapter>();

        builder.Services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<XionCommerceAdapter>());

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

        builder.Services.AddScoped<AgentConfigProviderAccessor>(sp => () => sp.GetRequiredService<IAgentConfigProvider>());
        builder.Services.AddScoped<ExternalEscalationOutcomePublisherAccessor>(sp => () => sp.GetRequiredService<IExternalEscalationOutcomePublisher>());
        builder.Services.AddScoped<IExternalEscalationService, ExternalEscalationService>();

                builder.Services.AddScoped<IExternalEscalationOutcomePublisher, ExternalEscalationOutcomePublisher>();

        builder.Services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();

        builder.Services.AddScoped<IInboundMessageBatchProcessor, InboundMessageBatchProcessor>();

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

        builder.Services.AddScoped<IBlobStorageService>(sp =>

        {

            var client = sp.GetRequiredService<BlobServiceClient>();

            return new BlobStorageService(client, sp.GetRequiredService<ILogger<BlobStorageService>>());

        });

        builder.Services.AddSingleton(sp =>

        {
            return AzureManagedClientFactory.CreateBlobServiceClient(
                sp.GetRequiredService<IConfiguration>());

        });

        builder.Services.AddHttpClient();

        builder.Services.Configure<WhatsAppWebhookOptions>(builder.Configuration.GetSection(WhatsAppWebhookOptions.SectionName));

        builder.Services.AddScoped<IWhatsAppCredentialResolver, WhatsAppCredentialResolver>();

        builder.Services.AddHttpClient<IWhatsAppTemplateService, WhatsAppTemplateService>();

        builder.Services.AddScoped<IWhatsAppService>(sp =>

        {

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();

            var resolver = sp.GetRequiredService<IWhatsAppCredentialResolver>();

            var logger = sp.GetRequiredService<ILogger<WhatsAppService>>();

            var webhookOptions = sp.GetRequiredService<IOptions<WhatsAppWebhookOptions>>();

            return new WhatsAppService(httpClient, resolver, logger, webhookOptions);

        });

        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IPosSecuritySynchronizationWriter, SqlPosSecuritySynchronizationWriter>();

        builder.Services.AddScoped<IRoleService, RoleService>();

        builder.Services.AddScoped<IPermissionService, PermissionService>();

        builder.Services.AddScoped<IAuditService, AuditService>();

        builder.Services.AddScoped<ITenantProvisioningStore, SqlTenantProvisioningStore>();
        builder.Services.AddScoped<IPasswordRecoveryStore, SqlPasswordRecoveryStore>();
        builder.Services.AddScoped<PasswordRecoveryService>();
        builder.Services.AddScoped<IBusinessDefaultsProvisioner, SqlBusinessDefaultsProvisioner>();
        builder.Services.AddScoped<Auraly.Application.Tenants.TenantInvitationService>();
        builder.Services.AddScoped<ITenantService, TenantService>();
        builder.Services.AddScoped<ITenantDeviceAdminStore, SqlTenantDeviceAdminStore>();

        builder.Services.AddScoped<IBusinessAdminService, BusinessAdminService>();

        builder.Services.AddScoped<IServiceAdminService, ServiceAdminService>();

        builder.Services.AddScoped<IProductAdminService, ProductAdminService>();
        builder.Services.AddScoped<IProductOfferAdminService, ProductOfferAdminService>();
        builder.Services.AddScoped<IProductAliasAdminService, ProductAliasAdminService>();
        builder.Services.AddScoped<IProductCatalogAdminService, ProductCatalogAdminService>();

        builder.Services.AddScoped<IPromotionAdminService, PromotionAdminService>();

        builder.Services.AddScoped<IEmployeeAdminService, EmployeeAdminService>();

        builder.Services.AddScoped<IReservationAdminService, ReservationAdminService>();

        builder.Services.AddScoped<ILeadAdminService, LeadAdminService>();

        builder.Services.AddScoped<ICampaignAdminService, CampaignAdminService>();

        builder.Services.AddScoped<ICampaignDispatchService, CampaignDispatchService>();

        builder.Services.AddScoped<IIntegrationAdminService, IntegrationAdminService>();

        builder.Services.AddScoped<IWorkingHoursAdminService, WorkingHoursAdminService>();

        builder.Services.AddScoped<IAgentRepository, AgentRepository>();

        builder.Services.AddScoped<IAgentAdminService, AgentAdminService>();
        builder.Services.AddHttpClient<Auraly.Platform.Application.Identity.Interfaces.IWhatsAppChannelAdminService,
            Auraly.Platform.Infrastructure.Services.WhatsAppChannelAdminService>();

        builder.Services.AddScoped<ICatalogImportAdminService, CatalogImportAdminService>();

        builder.Services.AddScoped<ICatalogDocumentTextExtractor, CatalogDocumentTextExtractor>();
        builder.Services.AddScoped<IInboundDocumentTextExtractor, InboundDocumentTextExtractor>();

        builder.Services.AddScoped<ICatalogDraftParser, CatalogDraftAiParser>();

        builder.Services.Configure<OpenAITextModelOptions>(builder.Configuration.GetSection(OpenAITextModelOptions.SectionName));

        builder.Services.AddSingleton<AzureOpenAIClient>(sp =>

        {

            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAITextModelOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.Endpoint))

                throw new InvalidOperationException("OpenAI:TextModel:Endpoint debe estar configurado en Auraly.Api.");

            return AzureManagedClientFactory.CreateAzureOpenAIClient(
                builder.Configuration, options.Endpoint, options.ApiKey);

        });

        builder.Services.AddScoped<IChatClient>(sp =>

        {

            var client = sp.GetRequiredService<AzureOpenAIClient>();

            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAITextModelOptions>>().Value;

            var logger = sp.GetRequiredService<ILogger<AzureOpenAIChatClient>>();

            return new AzureOpenAIChatClient(client, options.DeploymentName, logger);

        });

        builder.Services.AddScoped<IAgentConfigProvider, AgentConfigProvider>();

        builder.Services.AddSingleton<IFactSourceResolver, ChannelPhoneResolver>();

        builder.Services.AddSingleton<IFactSourceResolver, ChannelEmailResolver>();

        builder.Services.AddSingleton<IFactHydrator, FactHydrator>();

        builder.Services.AddScoped<IConversationVerificationService, ConversationVerificationService>();

        builder.Services.AddScoped<IOperatingHoursTurnPolicy, OperatingHoursTurnPolicy>();

        builder.Services.AddScoped<IAgentTemplateResolver, AgentTemplateResolver>();

        builder.Services.AddScoped<ITemplateRenderer, PromptTemplateRenderer>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Availability.CheckAvailabilityOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Catalog.GetCompatibleAddOnsOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Catalog.GetServiceFulfillmentOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Catalog.ResolveServiceSelectionOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Catalog.GetServiceCatalogOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.SearchProductsOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.SearchProductOffersOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.SearchRecipesOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.ApplyOrderChangesOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.Reservation.IReservationCheckoutPreparationService, Auraly.Platform.Application.Agents.Operations.Reservation.ReservationCheckoutPreparationService>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.CreateCommerceOrderOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.PrepareCommerceCheckoutOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Reservation.PrepareReservationCheckoutOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Reservation.ManageReservationOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.Reservation.IReservationCreationService, Auraly.Platform.Application.Agents.Operations.Reservation.ReservationCreationService>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Reservation.CreateReservationOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Reservation.ListCustomerReservationsOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.GetOrderDraftOperation>();
        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Conversation.GetKnownFactsOperation>();
        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Conversation.CompleteConversationRequestOperation>();
        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Checkout.ListPaymentMethodsOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Escalation.RequestHumanEscalationOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Conversation.ResetConversationRequestOperation>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.AgentOperationRegistry>();

        Auraly.Platform.Application.Agents.Operations.Internal.InternalOperationRegistration.AddInternalAgentOperations(builder.Services);

        builder.Services.AddSingleton<Auraly.Platform.Application.Agents.Facts.FactMutationBatchProcessor>();

        builder.Services.AddSingleton<Auraly.Platform.Application.Agents.Runtime.IDeterministicFlowSelector, Auraly.Platform.Application.Agents.Runtime.DeterministicFlowSelector>();

        builder.Services.AddScoped<Auraly.Platform.Application.Commerce.ICartProductResolver, Auraly.Platform.Application.Commerce.CommerceCartProductResolver>();
        builder.Services.AddScoped<Auraly.Platform.Application.Commerce.IProductCandidateRetriever, Auraly.Platform.Application.Commerce.LocalProductCandidateRetriever>();
        builder.Services.AddScoped<Auraly.Platform.Application.Commerce.IProductAliasService, Auraly.Platform.Application.Commerce.ProductAliasService>();
        builder.Services.AddScoped<Auraly.Platform.Application.Commerce.IProductCatalogSyncService, Auraly.Platform.Application.Commerce.ProductCatalogSyncService>();

        builder.Services.AddScoped<Auraly.Platform.Application.Commerce.ICartMutationStore, Auraly.Platform.Application.Commerce.CommerceCartMutationStore>();

        builder.Services.AddScoped<Auraly.Platform.Application.Commerce.CartCommandBatchProcessor>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Configuration.AgentConfigurationCompiler>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.StageConditionEvaluator>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.OperationArgumentBinder>();

        builder.Services.AddSingleton<Auraly.Platform.Application.Agents.Planning.TurnPlanValidator>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Planning.ITurnPlanningContextEnricher, Auraly.Platform.Application.Agents.Planning.CommerceCartPlanningContextEnricher>();
        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Planning.ITurnPlanningContextEnricher, Auraly.Platform.Application.Agents.Planning.CommerceSelectionPlanningContextEnricher>();
        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Planning.ITurnPlanner, Auraly.Platform.Application.Agents.Planning.LlmTurnPlanner>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.DeterministicStageExecutor>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.DeterministicStageTransitionResolver>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.DeterministicTurnCoordinator>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IDeterministicResponseRenderer, Auraly.Platform.Application.Agents.Runtime.DeterministicResponseRenderer>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IOperationEventContextResolver, Auraly.Platform.Application.Agents.Runtime.ReservationCreatedOperationEventContextResolver>();
        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IOperationEventContextResolver, Auraly.Platform.Application.Agents.Runtime.OrderCreatedOperationEventContextResolver>();
        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IOperationEventContextResolver, Auraly.Platform.Application.Agents.Runtime.ManualPaymentRequestedOperationEventContextResolver>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IDeterministicTurnEffectProcessor, Auraly.Platform.Application.Agents.Runtime.DeterministicTurnEffectProcessor>();

        builder.Services.AddScoped<Auraly.Platform.Application.Agents.Operations.IOperationPresentationComposer, Auraly.Platform.Application.Agents.Operations.OperationPresentationComposer>();

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

                Title = "Auraly API",

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
    }

    public static async Task SeedPlatformPermissionsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        await permissionService.SeedPermissionsAsync();
    }
}
