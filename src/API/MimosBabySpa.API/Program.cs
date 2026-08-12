using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Microsoft.Azure.Functions.Worker;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Logging;

using MimosBabySpa.Application.Services;

using MimosBabySpa.Application.Promotions;

using MimosBabySpa.Domain.Repositories;

using MimosBabySpa.Infrastructure.Data;

using MimosBabySpa.Infrastructure.Repositories;

using MimosBabySpa.Infrastructure.Services;

using MimosBabySpa.Infrastructure.Configuration;

using Azure.Storage.Blobs;

using Azure.AI.OpenAI;

using Azure.Identity;

using Microsoft.Extensions.Options;

using MimosBabySpa.Application.BusinessRules;

using MimosBabySpa.Application.Billing;

using MimosBabySpa.Application.Campaigns.Interfaces;

using MimosBabySpa.Application.Campaigns.Services;

using MimosBabySpa.Application.Commerce;

using MimosBabySpa.Application.Configuration;

using MimosBabySpa.Application.StateManagement;

// Deterministic Agent Engine

using MimosBabySpa.Application.Agents;

using MimosBabySpa.Application.Agents.Composition;

using MimosBabySpa.Application.Agents.Facts;

using MimosBabySpa.Application.Agents.Gating;

using MimosBabySpa.Application.Agents.Runtime;

using MimosBabySpa.Application.Agents.Operations.Support;

using MimosBabySpa.Application.LLM;

using MimosBabySpa.Application.Agents.Templates;

using MimosBabySpa.Application.Time;

using MimosBabySpa.Infrastructure.Commerce;

using MimosBabySpa.Infrastructure.LLM;

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

        services.AddDbContext<ApplicationDbContext>(options =>

            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
        services.AddExternalCustomerReconciliationMessaging(configuration);

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

        services.AddScoped<MimosBabySpa.Application.Identity.Interfaces.ICatalogDocumentTextExtractor,
            MimosBabySpa.Infrastructure.Catalog.CatalogDocumentTextExtractor>();
        services.AddScoped<IInboundDocumentTextExtractor,
            MimosBabySpa.Infrastructure.Catalog.InboundDocumentTextExtractor>();

        services.AddScoped<IChatClient>(sp =>

        {

            var textClient = sp.GetRequiredKeyedService<AzureOpenAIClient>("Text");

            var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;

            var logger = sp.GetRequiredService<ILogger<AzureOpenAIChatClient>>();

            return new AzureOpenAIChatClient(textClient, textOptions.DeploymentName, logger);

        });

        services.AddScoped<IAgentConfigProvider, AgentConfigProvider>();

        // Hydrator: plugin model

        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelPhoneResolver>();

        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelEmailResolver>();

        services.AddSingleton<IFactHydrator, FactHydrator>();

        services.AddScoped<IConversationVerificationService, ConversationVerificationService>();

        services.AddScoped<IOperatingHoursTurnPolicy, OperatingHoursTurnPolicy>();

        services.AddScoped<IAgentTemplateResolver, AgentTemplateResolver>();

        services.AddScoped<ITemplateRenderer, PromptTemplateRenderer>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Availability.CheckAvailabilityOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetCompatibleAddOnsOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetServiceFulfillmentOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.ResolveServiceSelectionOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetServiceCatalogOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.SearchProductsOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.SearchProductOffersOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.SearchRecipesOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.ApplyOrderChangesOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.Reservation.IReservationCheckoutPreparationService, MimosBabySpa.Application.Agents.Operations.Reservation.ReservationCheckoutPreparationService>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.CreateCommerceOrderOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.PrepareCommerceCheckoutOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.PrepareReservationCheckoutOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.ManageReservationOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.Reservation.IReservationCreationService, MimosBabySpa.Application.Agents.Operations.Reservation.ReservationCreationService>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.CreateReservationOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.ListCustomerReservationsOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.GetOrderDraftOperation>();
services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Conversation.GetKnownFactsOperation>();
services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Conversation.CompleteConversationRequestOperation>();
services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Checkout.ListPaymentMethodsOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Escalation.RequestHumanEscalationOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Conversation.ResetConversationRequestOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.AgentOperationRegistry>();

MimosBabySpa.Application.Agents.Operations.Internal.InternalOperationRegistration.AddInternalAgentOperations(services);

services.AddSingleton<MimosBabySpa.Application.Agents.Facts.FactMutationBatchProcessor>();

services.AddSingleton<MimosBabySpa.Application.Agents.Runtime.IDeterministicFlowSelector, MimosBabySpa.Application.Agents.Runtime.DeterministicFlowSelector>();

services.AddScoped<MimosBabySpa.Application.Commerce.ICartProductResolver, MimosBabySpa.Application.Commerce.CommerceCartProductResolver>();
services.AddScoped<MimosBabySpa.Application.Commerce.IProductCandidateRetriever, MimosBabySpa.Application.Commerce.LocalProductCandidateRetriever>();
services.AddScoped<MimosBabySpa.Application.Commerce.IProductCatalogSyncService, MimosBabySpa.Application.Commerce.ProductCatalogSyncService>();
services.AddScoped<MimosBabySpa.Application.Commerce.IProductAliasService, MimosBabySpa.Application.Commerce.ProductAliasService>();

services.AddScoped<MimosBabySpa.Application.Commerce.ICartMutationStore, MimosBabySpa.Application.Commerce.CommerceCartMutationStore>();

services.AddScoped<MimosBabySpa.Application.Commerce.CartCommandBatchProcessor>();

services.AddScoped<MimosBabySpa.Application.Agents.Configuration.AgentConfigurationCompiler>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.StageConditionEvaluator>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.OperationArgumentBinder>();

services.AddSingleton<MimosBabySpa.Application.Agents.Planning.TurnPlanValidator>();

services.AddScoped<MimosBabySpa.Application.Agents.Planning.ITurnPlanningContextEnricher, MimosBabySpa.Application.Agents.Planning.CommerceCartPlanningContextEnricher>();
services.AddScoped<MimosBabySpa.Application.Agents.Planning.ITurnPlanningContextEnricher, MimosBabySpa.Application.Agents.Planning.CommerceSelectionPlanningContextEnricher>();
services.AddScoped<MimosBabySpa.Application.Agents.Planning.ITurnPlanner, MimosBabySpa.Application.Agents.Planning.LlmTurnPlanner>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicStageExecutor>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicStageTransitionResolver>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicTurnCoordinator>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IDeterministicResponseRenderer, MimosBabySpa.Application.Agents.Runtime.DeterministicResponseRenderer>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IOperationEventContextResolver, MimosBabySpa.Application.Agents.Runtime.ReservationCreatedOperationEventContextResolver>();
services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IOperationEventContextResolver, MimosBabySpa.Application.Agents.Runtime.OrderCreatedOperationEventContextResolver>();
services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IOperationEventContextResolver, MimosBabySpa.Application.Agents.Runtime.ManualPaymentRequestedOperationEventContextResolver>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IDeterministicTurnEffectProcessor, MimosBabySpa.Application.Agents.Runtime.DeterministicTurnEffectProcessor>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IOperationPresentationComposer, MimosBabySpa.Application.Agents.Operations.OperationPresentationComposer>();

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
