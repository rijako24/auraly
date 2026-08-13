using System.Text;
using Auraly.Application.Authentication;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
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

using MimosBabySpa.Application.Auth.Interfaces;

using MimosBabySpa.Application.Auth.Services;

using MimosBabySpa.Application.Agents;

using MimosBabySpa.Application.Agents.Composition;

using MimosBabySpa.Application.Agents.Facts;

using MimosBabySpa.Application.Agents.Facts.Resolvers;

using MimosBabySpa.Application.Agents.Gating;

using MimosBabySpa.Application.Agents.Runtime;

using MimosBabySpa.Application.Agents.Templates;

using MimosBabySpa.Application.Agents.Testing;

using MimosBabySpa.Application.Agents.Operations.Support;

using MimosBabySpa.Application.Billing;

using MimosBabySpa.Application.Campaigns.Interfaces;

using MimosBabySpa.Application.Campaigns.Services;

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

using MimosBabySpa.Application.WhatsAppTemplates.Interfaces;

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
        bool configureAuthentication = true,
        bool configureExternalCustomerMessaging = true)
    {
        var connectionString =
            builder.Configuration.GetConnectionString("Auraly")
            ?? builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Auraly or ConnectionStrings:DefaultConnection is required.");
        }

        builder.Services.AddDbContext<ApplicationDbContext>(options =>

            options.UseSqlServer(connectionString));

        builder.Services.AddMemoryCache();
        if (configureExternalCustomerMessaging)
        {
            builder.Services.AddExternalCustomerReconciliationMessaging(builder.Configuration);
        }
        else
        {
            builder.Services.AddSingleton<ExternalCustomerReconciliationOutboxSignal>();
            builder.Services.AddScoped<ExternalCustomerReconciliationCommitState>();
        }

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
        builder.Services.AddSingleton(new SqlServerConnectionFactory(connectionString));
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
        builder.Services.AddScoped<ICommerceCustomerResolver, CommerceCustomerResolver>();

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

        builder.Services.AddScoped<IRoleService, RoleService>();

        builder.Services.AddScoped<IPermissionService, PermissionService>();

        builder.Services.AddScoped<IAuditService, AuditService>();

        builder.Services.AddScoped<ITenantProvisioningStore, SqlTenantProvisioningStore>();
        builder.Services.AddScoped<IBusinessDefaultsProvisioner, SqlBusinessDefaultsProvisioner>();
        builder.Services.AddScoped<Auraly.Application.Tenants.TenantInvitationService>();
        builder.Services.AddScoped<ITenantService, TenantService>();

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
        builder.Services.AddHttpClient<MimosBabySpa.Application.Identity.Interfaces.IWhatsAppChannelAdminService,
            MimosBabySpa.Infrastructure.Services.WhatsAppChannelAdminService>();

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

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Availability.CheckAvailabilityOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetCompatibleAddOnsOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetServiceFulfillmentOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.ResolveServiceSelectionOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetServiceCatalogOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.SearchProductsOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.SearchProductOffersOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.SearchRecipesOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.ApplyOrderChangesOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.Reservation.IReservationCheckoutPreparationService, MimosBabySpa.Application.Agents.Operations.Reservation.ReservationCheckoutPreparationService>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.CreateCommerceOrderOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.PrepareCommerceCheckoutOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.PrepareReservationCheckoutOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.ManageReservationOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.Reservation.IReservationCreationService, MimosBabySpa.Application.Agents.Operations.Reservation.ReservationCreationService>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.CreateReservationOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.ListCustomerReservationsOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.GetOrderDraftOperation>();
        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Conversation.GetKnownFactsOperation>();
        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Conversation.CompleteConversationRequestOperation>();
        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Checkout.ListPaymentMethodsOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Escalation.RequestHumanEscalationOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Conversation.ResetConversationRequestOperation>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.AgentOperationRegistry>();

        MimosBabySpa.Application.Agents.Operations.Internal.InternalOperationRegistration.AddInternalAgentOperations(builder.Services);

        builder.Services.AddSingleton<MimosBabySpa.Application.Agents.Facts.FactMutationBatchProcessor>();

        builder.Services.AddSingleton<MimosBabySpa.Application.Agents.Runtime.IDeterministicFlowSelector, MimosBabySpa.Application.Agents.Runtime.DeterministicFlowSelector>();

        builder.Services.AddScoped<MimosBabySpa.Application.Commerce.ICartProductResolver, MimosBabySpa.Application.Commerce.CommerceCartProductResolver>();
        builder.Services.AddScoped<MimosBabySpa.Application.Commerce.IProductCandidateRetriever, MimosBabySpa.Application.Commerce.LocalProductCandidateRetriever>();
        builder.Services.AddScoped<MimosBabySpa.Application.Commerce.IProductAliasService, MimosBabySpa.Application.Commerce.ProductAliasService>();
        builder.Services.AddScoped<MimosBabySpa.Application.Commerce.IProductCatalogSyncService, MimosBabySpa.Application.Commerce.ProductCatalogSyncService>();

        builder.Services.AddScoped<MimosBabySpa.Application.Commerce.ICartMutationStore, MimosBabySpa.Application.Commerce.CommerceCartMutationStore>();

        builder.Services.AddScoped<MimosBabySpa.Application.Commerce.CartCommandBatchProcessor>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Configuration.AgentConfigurationCompiler>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.StageConditionEvaluator>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.OperationArgumentBinder>();

        builder.Services.AddSingleton<MimosBabySpa.Application.Agents.Planning.TurnPlanValidator>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Planning.ITurnPlanningContextEnricher, MimosBabySpa.Application.Agents.Planning.CommerceCartPlanningContextEnricher>();
        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Planning.ITurnPlanningContextEnricher, MimosBabySpa.Application.Agents.Planning.CommerceSelectionPlanningContextEnricher>();
        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Planning.ITurnPlanner, MimosBabySpa.Application.Agents.Planning.LlmTurnPlanner>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicStageExecutor>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicStageTransitionResolver>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicTurnCoordinator>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IDeterministicResponseRenderer, MimosBabySpa.Application.Agents.Runtime.DeterministicResponseRenderer>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IOperationEventContextResolver, MimosBabySpa.Application.Agents.Runtime.ReservationCreatedOperationEventContextResolver>();
        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IOperationEventContextResolver, MimosBabySpa.Application.Agents.Runtime.OrderCreatedOperationEventContextResolver>();
        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IOperationEventContextResolver, MimosBabySpa.Application.Agents.Runtime.ManualPaymentRequestedOperationEventContextResolver>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IDeterministicTurnEffectProcessor, MimosBabySpa.Application.Agents.Runtime.DeterministicTurnEffectProcessor>();

        builder.Services.AddScoped<MimosBabySpa.Application.Agents.Operations.IOperationPresentationComposer, MimosBabySpa.Application.Agents.Operations.OperationPresentationComposer>();

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
