using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Microsoft.Azure.Functions.Worker;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Logging;

using Auraly.Platform.Application.Services;

using Auraly.Platform.Application.Promotions;

using Auraly.Platform.Domain.Repositories;

using Auraly.Platform.Infrastructure.Data;

using Auraly.Platform.Infrastructure.Repositories;

using Auraly.Platform.Infrastructure.Services;

using Auraly.Platform.Infrastructure.Configuration;

using Azure.Storage.Blobs;

using Azure.AI.OpenAI;

using Azure.Identity;

using Microsoft.Extensions.Options;

using Auraly.Platform.Application.BusinessRules;

using Auraly.Platform.Application.Billing;

using Auraly.Platform.Application.Campaigns.Interfaces;

using Auraly.Platform.Application.Campaigns.Services;

using Auraly.Platform.Application.Commerce;

using Auraly.Platform.Application.Configuration;

using Auraly.Platform.Application.StateManagement;

// Deterministic Agent Engine

using Auraly.Platform.Application.Agents;

using Auraly.Platform.Application.Agents.Composition;

using Auraly.Platform.Application.Agents.Facts;

using Auraly.Platform.Application.Agents.Gating;

using Auraly.Platform.Application.Agents.Runtime;

using Auraly.Platform.Application.Agents.Operations.Support;

using Auraly.Platform.Application.LLM;

using Auraly.Platform.Application.Agents.Templates;

using Auraly.Platform.Application.Time;

using Auraly.Platform.Infrastructure.Commerce;

using Auraly.Platform.Infrastructure.LLM;
using Auraly.Application.Parties;
using Auraly.Infrastructure.Persistence;

var host = new HostBuilder()

    .ConfigureFunctionsWorkerDefaults()

    .ConfigureAppConfiguration((_, configuration) =>
    {
        var bootstrap = configuration.Build();
        var endpoint = bootstrap["AppConfiguration:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            configuration.AddAzureAppConfiguration(options =>
                options.Connect(
                    new Uri(endpoint),
                    AzureManagedClientFactory.CreateCredential(
                        bootstrap, "AppConfiguration:ManagedIdentityClientId")));
        }
    })

    .ConfigureServices((context, services) =>

    {

        var configuration = context.Configuration;

        // Database

        var databaseConnection = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required.");
        services.AddDbContext<ApplicationDbContext>(options =>

            options.UseSqlServer(databaseConnection));

        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
        services.AddSingleton(new SqlServerConnectionFactory(databaseConnection));

        // Repositories

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IConversationRepository, ConversationRepository>();

        services.AddScoped<IMessageRepository, MessageRepository>();

        services.AddScoped<ILeadRepository, LeadRepository>();

        services.AddScoped<ICampaignRepository, CampaignRepository>();

        services.AddScoped<IReservationRepository, ReservationRepository>();

        services.AddScoped<IConversationStateRepository, ConversationStateRepository>();

        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();

        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();

        services.AddScoped<IAgentRepository, AgentRepository>();

        // Application Services

        services.AddScoped<IConversationService, ConversationService>();

        services.AddScoped<IConversationLifecycleService, ConversationLifecycleService>();

        services.AddScoped<IMessageService, MessageService>();

        services.AddScoped<ILeadService, LeadService>();

        services.AddScoped<ICampaignDispatchService, CampaignDispatchService>();

        services.AddScoped<IReservationService, ReservationService>();

        services.AddScoped<IBusinessIdentificationService, BusinessIdentificationService>();

        services.AddScoped<IWhatsAppWebhookParserService, WhatsAppWebhookParserService>();

        services.AddScoped<IInboundMessageDeduplicationService, InboundMessageDeduplicationService>();

        services.AddSingleton<IWhatsAppInboundQueueService, WhatsAppInboundQueueService>();

        services.AddSingleton<ICampaignQueueService, CampaignQueueService>();

        services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();

        services.AddScoped<IWorkingHoursService, WorkingHoursService>();

        services.AddScoped<IAvailabilityService, AvailabilityService>();

        services.AddScoped<ServiceNameResolver>();

services.AddScoped<ServiceSelectionResolver>();

        services.AddScoped<ReservationPricingResolver>();

        services.AddScoped<IPromotionPricingService, PromotionPricingService>();

        services.AddScoped<IServiceCatalogPricingService, ServiceCatalogPricingService>();

        services.AddScoped<IBusinessClock, BusinessClock>();

        services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();

        services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();

        services.AddScoped<IAddOnCatalogService, AddOnCatalogService>();

        services.AddScoped<IUsageBillingService, UsageBillingService>();

        services.AddScoped<IProductCatalogAvailabilityService, ProductCatalogAvailabilityService>();

        services.AddScoped<ICommerceService, CommerceService>();
        services.AddScoped<ICommerceOrderWorkspaceResolver, Auraly.Platform.Infrastructure.Commerce.CommerceOrderWorkspaceResolver>();
        services.AddScoped<ICommerceCustomerResolver, CommerceCustomerResolver>();

        services.AddScoped<IProductLookupService>(provider => (IProductLookupService)provider.GetRequiredService<ICommerceService>());

        services.AddScoped<ICatalogRecommendationService, CatalogRecommendationService>();

        services.AddScoped<ICommerceAdapter, LocalCommerceAdapter>();

        services.AddHttpClient<SiigoCommerceAdapter>();

        services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<SiigoCommerceAdapter>());

        services.AddHttpClient<MantisCommerceAdapter>();

        services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<MantisCommerceAdapter>());

        services.AddHttpClient<XionCommerceAdapter>();

        services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<XionCommerceAdapter>());

        services.AddScoped<ICommerceAdapterFactory, CommerceAdapterFactory>();

        // OpenAI Clients

        services.Configure<OpenAITextModelOptions>(configuration.GetSection(OpenAITextModelOptions.SectionName));

        services.Configure<OpenAIAudioModelOptions>(configuration.GetSection(OpenAIAudioModelOptions.SectionName));

        services.AddSingleton(_ =>

            configuration.GetSection(AudioTranscriptionQualityOptions.SectionName).Get<AudioTranscriptionQualityOptions>()

            ?? new AudioTranscriptionQualityOptions());

        services.AddSingleton<IAudioTranscriptionQualityEvaluator, AudioTranscriptionQualityEvaluator>();

        services.AddKeyedSingleton<AzureOpenAIClient>("Text", (sp, _) =>

        {

            var options = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.Endpoint))

                throw new InvalidOperationException("OpenAI:TextModel:Endpoint debe estar configurado");

            if (string.IsNullOrEmpty(options.DeploymentName))

                throw new InvalidOperationException("OpenAI:TextModel:DeploymentName debe estar configurado");

            return AzureManagedClientFactory.CreateAzureOpenAIClient(
                configuration,
                options.Endpoint,
                options.ApiKey);

        });

        services.AddKeyedSingleton<AzureOpenAIClient>("Audio", (sp, _) =>

        {

            var options = sp.GetRequiredService<IOptions<OpenAIAudioModelOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.Endpoint))

                throw new InvalidOperationException("OpenAI:AudioModel:Endpoint debe estar configurado");

            if (string.IsNullOrEmpty(options.DeploymentName))

                throw new InvalidOperationException("OpenAI:AudioModel:DeploymentName debe estar configurado");

            return AzureManagedClientFactory.CreateAzureOpenAIClient(
                configuration,
                options.Endpoint,
                options.ApiKey);

        });

        // AI Service (audio transcription with Whisper)

        services.AddScoped<IAIService>(sp =>

        {

            var audioClient = sp.GetRequiredKeyedService<AzureOpenAIClient>("Audio");

            var audioOptions = sp.GetRequiredService<IOptions<OpenAIAudioModelOptions>>().Value;

            var logger = sp.GetRequiredService<ILogger<AIService>>();

            return new AIService(audioClient, audioOptions.DeploymentName, logger);

        });

        // State Management

        services.AddScoped<IConversationStateManager, ConversationStateManager>();

        // Business Rules Engine

        services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();

        // Supporting services

        services.AddSingleton<ILocalizationService, LocalizationService>();

        // Payment Link Service (Wompi)

        services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();

        // Payment Confirmation Handler (Webhook)

        services.AddScoped<IPaymentConfirmationHandler, PaymentConfirmationHandler>();

        services.AddScoped<IMediaUrlResolver, BlobMediaUrlResolver>();

        services.AddScoped<IOutboundMessageDispatcher, OutboundMessageDispatcher>();
        services.AddScoped<ConversationFollowUpService>();
        services.AddScoped<IConversationFollowUpService>(sp =>
            sp.GetRequiredService<ConversationFollowUpService>());

        services.AddScoped<IMessageSequenceResolver, MessageSequenceResolver>();

        services.AddScoped<IActiveAgentConfigResolver, ActiveAgentConfigResolver>();

        services.AddScoped<IEventNotificationDispatcher, EventNotificationDispatcher>();

        services.AddScoped<IConversationFactsService, ConversationFactsService>();

        services.AddScoped<ICustomerMemoryService, CustomerMemoryService>();

        services.AddScoped<IRequestContextService, RequestContextService>();

        services.AddScoped<IReservationLifecycleService, ReservationLifecycleService>();

        services.AddScoped<ICustomerReservationResolver, CustomerReservationResolver>();

        services.AddScoped<IPaymentLifecycleService, PaymentLifecycleService>();

        services.AddScoped<IReservationIntentBuilder, ReservationIntentBuilder>();

        services.AddScoped<ICheckoutQuoteService, CheckoutQuoteService>();

        services.AddScoped<ICheckoutPaymentCoordinator, CheckoutPaymentCoordinator>();

        services.AddScoped<IPaidCheckoutFulfillmentRegistry, PaidCheckoutFulfillmentRegistry>();

        services.AddScoped<IPaidCheckoutFulfillmentHandler, ReservationPaidCheckoutFulfillmentHandler>();

        services.AddScoped<IPaidCheckoutFulfillmentHandler, EnrollmentPaidCheckoutFulfillmentHandler>();

        services.AddScoped<IPaidCheckoutFulfillmentHandler, OrderPaidCheckoutFulfillmentHandler>();

        // Webhook signature validation (Wompi)

        services.AddSingleton<IWompiWebhookSignatureValidator, WompiWebhookSignatureValidator>();

        // Escalation y release (handover a humano)

        services.AddScoped<IEscalationNotifier, EscalationNotifier>();

        services.AddScoped<IEscalationConfigProvider, EscalationConfigProvider>();

        services.AddScoped<IReleaseLinkService, ReleaseLinkService>();

        services.AddScoped<IConversationReleaseService, ConversationReleaseService>();

        services.AddScoped<IBusinessInboundContactRouter, BusinessInboundContactRouter>();

        services.AddScoped<AgentConfigProviderAccessor>(sp => () => sp.GetRequiredService<IAgentConfigProvider>());
services.AddScoped<ExternalEscalationOutcomePublisherAccessor>(sp => () => sp.GetRequiredService<IExternalEscalationOutcomePublisher>());
services.AddScoped<IExternalEscalationService, ExternalEscalationService>();

        services.AddScoped<IExternalEscalationOutcomePublisher, ExternalEscalationOutcomePublisher>();

        services.AddScoped<IInboundMessageBatchProcessor, InboundMessageBatchProcessor>();

        services.AddScoped<ITimedProcessScheduleProvider, SystemConfigurationTimedProcessScheduleProvider>();

        services.AddSingleton<ITimedProcessSchedulePolicy, TimedProcessSchedulePolicy>();

        services.AddScoped<ITimedProcessScheduler, TimedProcessScheduler>();

        services.AddScoped<ITimedProcess, PaymentLinkPollingProcess>();

        services.AddScoped<ITimedProcess, ExternalEscalationExpirationProcess>();

        services.AddScoped<ITimedProcess, ReservationAutomationProcess>();
        services.AddScoped<ITimedProcess>(sp =>
            sp.GetRequiredService<ConversationFollowUpService>());

        // -- DETERMINISTIC AGENT ENGINE -------------------------------------

        services.AddScoped<Auraly.Platform.Application.Identity.Interfaces.ICatalogDocumentTextExtractor,
            Auraly.Platform.Infrastructure.Catalog.CatalogDocumentTextExtractor>();
        services.AddScoped<IInboundDocumentTextExtractor,
            Auraly.Platform.Infrastructure.Catalog.InboundDocumentTextExtractor>();

        services.AddScoped<IChatClient>(sp =>

        {

            var textClient = sp.GetRequiredKeyedService<AzureOpenAIClient>("Text");

            var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;

            var logger = sp.GetRequiredService<ILogger<AzureOpenAIChatClient>>();

            return new AzureOpenAIChatClient(textClient, textOptions.DeploymentName, logger);

        });

        services.AddScoped<IAgentConfigProvider, AgentConfigProvider>();

        // Hydrator: plugin model

        services.AddSingleton<IFactSourceResolver, Auraly.Platform.Application.Agents.Facts.Resolvers.ChannelPhoneResolver>();

        services.AddSingleton<IFactSourceResolver, Auraly.Platform.Application.Agents.Facts.Resolvers.ChannelEmailResolver>();

        services.AddSingleton<IFactHydrator, FactHydrator>();

        services.AddScoped<IConversationVerificationService, ConversationVerificationService>();

        services.AddScoped<IOperatingHoursTurnPolicy, OperatingHoursTurnPolicy>();

        services.AddScoped<IAgentTemplateResolver, AgentTemplateResolver>();

        services.AddScoped<ITemplateRenderer, PromptTemplateRenderer>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Availability.CheckAvailabilityOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Catalog.GetCompatibleAddOnsOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Catalog.GetServiceFulfillmentOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Catalog.ResolveServiceSelectionOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Catalog.GetServiceCatalogOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.SearchProductsOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.SearchProductOffersOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.SearchRecipesOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.ApplyOrderChangesOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.Reservation.IReservationCheckoutPreparationService, Auraly.Platform.Application.Agents.Operations.Reservation.ReservationCheckoutPreparationService>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.CreateCommerceOrderOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.PrepareCommerceCheckoutOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Reservation.PrepareReservationCheckoutOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Reservation.ManageReservationOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.Reservation.IReservationCreationService, Auraly.Platform.Application.Agents.Operations.Reservation.ReservationCreationService>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Reservation.CreateReservationOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Reservation.ListCustomerReservationsOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Commerce.GetOrderDraftOperation>();
services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Conversation.GetKnownFactsOperation>();
services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Conversation.CompleteConversationRequestOperation>();
services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Checkout.ListPaymentMethodsOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Escalation.RequestHumanEscalationOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IAgentOperation, Auraly.Platform.Application.Agents.Operations.Conversation.ResetConversationRequestOperation>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.AgentOperationRegistry>();

Auraly.Platform.Application.Agents.Operations.Internal.InternalOperationRegistration.AddInternalAgentOperations(services);

services.AddSingleton<Auraly.Platform.Application.Agents.Facts.FactMutationBatchProcessor>();

services.AddSingleton<Auraly.Platform.Application.Agents.Runtime.IDeterministicFlowSelector, Auraly.Platform.Application.Agents.Runtime.DeterministicFlowSelector>();

services.AddScoped<Auraly.Platform.Application.Commerce.ICartProductResolver, Auraly.Platform.Application.Commerce.CommerceCartProductResolver>();
services.AddScoped<Auraly.Platform.Application.Commerce.IProductCandidateRetriever, Auraly.Platform.Application.Commerce.LocalProductCandidateRetriever>();
services.AddScoped<Auraly.Platform.Application.Commerce.IProductCatalogSyncService, Auraly.Platform.Application.Commerce.ProductCatalogSyncService>();
services.AddScoped<IExternalCustomerReconciliationStore, SqlExternalCustomerReconciliationStore>();
services.AddScoped<Auraly.Platform.Application.Commerce.IExternalCustomerReconciliationRunner, SqlExternalCustomerReconciliationRunner>();
services.AddScoped<Auraly.Platform.Application.Commerce.IProductAliasService, Auraly.Platform.Application.Commerce.ProductAliasService>();

services.AddScoped<Auraly.Platform.Application.Commerce.ICartMutationStore, Auraly.Platform.Application.Commerce.CommerceCartMutationStore>();

services.AddScoped<Auraly.Platform.Application.Commerce.CartCommandBatchProcessor>();

services.AddScoped<Auraly.Platform.Application.Agents.Configuration.AgentConfigurationCompiler>();

services.AddScoped<Auraly.Platform.Application.Agents.Runtime.StageConditionEvaluator>();

services.AddScoped<Auraly.Platform.Application.Agents.Runtime.OperationArgumentBinder>();

services.AddSingleton<Auraly.Platform.Application.Agents.Planning.TurnPlanValidator>();

services.AddScoped<Auraly.Platform.Application.Agents.Planning.ITurnPlanningContextEnricher, Auraly.Platform.Application.Agents.Planning.CommerceCartPlanningContextEnricher>();
services.AddScoped<Auraly.Platform.Application.Agents.Planning.ITurnPlanningContextEnricher, Auraly.Platform.Application.Agents.Planning.CommerceSelectionPlanningContextEnricher>();
services.AddScoped<Auraly.Platform.Application.Agents.Planning.ITurnPlanner, Auraly.Platform.Application.Agents.Planning.LlmTurnPlanner>();

services.AddScoped<Auraly.Platform.Application.Agents.Runtime.DeterministicStageExecutor>();

services.AddScoped<Auraly.Platform.Application.Agents.Runtime.DeterministicStageTransitionResolver>();

services.AddScoped<Auraly.Platform.Application.Agents.Runtime.DeterministicTurnCoordinator>();

services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IDeterministicResponseRenderer, Auraly.Platform.Application.Agents.Runtime.DeterministicResponseRenderer>();

services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IOperationEventContextResolver, Auraly.Platform.Application.Agents.Runtime.ReservationCreatedOperationEventContextResolver>();
services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IOperationEventContextResolver, Auraly.Platform.Application.Agents.Runtime.OrderCreatedOperationEventContextResolver>();
services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IOperationEventContextResolver, Auraly.Platform.Application.Agents.Runtime.ManualPaymentRequestedOperationEventContextResolver>();

services.AddScoped<Auraly.Platform.Application.Agents.Runtime.IDeterministicTurnEffectProcessor, Auraly.Platform.Application.Agents.Runtime.DeterministicTurnEffectProcessor>();

services.AddScoped<Auraly.Platform.Application.Agents.Operations.IOperationPresentationComposer, Auraly.Platform.Application.Agents.Operations.OperationPresentationComposer>();

        services.AddScoped<IAgentConversationService, AgentConversationService>();

        // WhatsAppMessageProcessorService (usa AgentConversationService)

        services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();

        // -- Infrastructure Services - WhatsApp -------------------------------------

        services.AddHttpClient();

        services.Configure<WhatsAppWebhookOptions>(configuration.GetSection(WhatsAppWebhookOptions.SectionName));

        services.AddScoped<IWhatsAppCredentialResolver, WhatsAppCredentialResolver>();

        services.AddScoped<IWhatsAppService>(sp =>

        {

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();

            var resolver = sp.GetRequiredService<IWhatsAppCredentialResolver>();

            var logger = sp.GetRequiredService<ILogger<WhatsAppService>>();

            var webhookOptions = sp.GetRequiredService<IOptions<WhatsAppWebhookOptions>>();

            return new WhatsAppService(httpClient, resolver, logger, webhookOptions);

        });

        // Infrastructure Services - Blob Storage

        services.AddSingleton(sp =>

        {

            return AzureManagedClientFactory.CreateBlobServiceClient(
                sp.GetRequiredService<IConfiguration>());

        });

        services.AddScoped<IBlobStorageService>(sp =>

        {

            var client = sp.GetRequiredService<BlobServiceClient>();

            return new BlobStorageService(client, sp.GetRequiredService<ILogger<BlobStorageService>>());

        });

        // Integrations Config Provider (Google Calendar, Wompi)

        services.AddScoped<IIntegrationsConfigProvider, IntegrationsConfigProvider>();

        services.AddScoped<ISchedulingPolicyProvider, SchedulingPolicyProvider>();

        // Release Link

        services.Configure<ReleaseLinkSettings>(configuration.GetSection(ReleaseLinkSettings.SectionName));

        // Calendar

        services.AddHttpClient<GoogleCalendarService>(client =>

        {

            client.Timeout = TimeSpan.FromSeconds(30);

        });

        services.AddScoped<ICalendarService, GoogleCalendarService>();

    })

    .Build();

host.Run();
