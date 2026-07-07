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

using Microsoft.Extensions.Options;



using MimosBabySpa.Application.BusinessRules;

using MimosBabySpa.Application.Billing;

using MimosBabySpa.Application.Campaigns.Interfaces;

using MimosBabySpa.Application.Campaigns.Services;

using MimosBabySpa.Application.Commerce;

using MimosBabySpa.Application.Configuration;

using MimosBabySpa.Application.StateManagement;



// Agentic Engine (Function Calling)

using MimosBabySpa.Application.Agents;

using MimosBabySpa.Application.Agents.Composition;

using MimosBabySpa.Application.Agents.Facts;

using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Runtime;

using MimosBabySpa.Application.Agents.Tools;

using MimosBabySpa.Application.Agents.Tools.Impl;

using MimosBabySpa.Application.LLM;

using MimosBabySpa.Application.Agents.Templates;

using MimosBabySpa.Application.Time;

using MimosBabySpa.Infrastructure.Commerce;

using MimosBabySpa.Infrastructure.LLM;



var host = new HostBuilder()

    .ConfigureFunctionsWorkerDefaults()

    .ConfigureServices((context, services) =>

    {

        var configuration = context.Configuration;



        // Database

        services.AddDbContext<ApplicationDbContext>(options =>

            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));



        services.AddMemoryCache();

        services.AddSingleton<AgentToolMetadataRegistry>();



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

        services.AddScoped<IBusinessClock, BusinessClock>();

        services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();

        services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();

        services.AddScoped<IAddOnCatalogService, AddOnCatalogService>();

        services.AddScoped<IUsageBillingService, UsageBillingService>();

        services.AddScoped<IProductCatalogAvailabilityService, ProductCatalogAvailabilityService>();

        services.AddScoped<ICommerceService, CommerceService>();

        services.AddScoped<ICommerceAdapter, LocalCommerceAdapter>();

        services.AddHttpClient<SiigoCommerceAdapter>();

        services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<SiigoCommerceAdapter>());

        services.AddScoped<ICommerceAdapterFactory, CommerceAdapterFactory>();



        // OpenAI Clients

        services.Configure<OpenAITextModelOptions>(configuration.GetSection(OpenAITextModelOptions.SectionName));

        services.Configure<OpenAIAudioModelOptions>(configuration.GetSection(OpenAIAudioModelOptions.SectionName));



        services.AddKeyedSingleton<OpenAIClient>("Text", (sp, _) =>

        {

            var options = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;

            if (string.IsNullOrEmpty(options.Endpoint) || string.IsNullOrEmpty(options.ApiKey))

                throw new InvalidOperationException("OpenAI:TextModel:Endpoint y ApiKey deben estar configurados");

            if (string.IsNullOrEmpty(options.DeploymentName))

                throw new InvalidOperationException("OpenAI:TextModel:DeploymentName debe estar configurado");

            return new OpenAIClient(new Uri(options.Endpoint), new Azure.AzureKeyCredential(options.ApiKey));

        });

        services.AddKeyedSingleton<OpenAIClient>("Audio", (sp, _) =>

        {

            var options = sp.GetRequiredService<IOptions<OpenAIAudioModelOptions>>().Value;

            if (string.IsNullOrEmpty(options.Endpoint) || string.IsNullOrEmpty(options.ApiKey))

                throw new InvalidOperationException("OpenAI:AudioModel:Endpoint y ApiKey deben estar configurados");

            if (string.IsNullOrEmpty(options.DeploymentName))

                throw new InvalidOperationException("OpenAI:AudioModel:DeploymentName debe estar configurado");

            return new OpenAIClient(new Uri(options.Endpoint), new Azure.AzureKeyCredential(options.ApiKey));

        });



        // AI Service (audio transcription with Whisper)

        services.AddScoped<IAIService>(sp =>

        {

            var audioClient = sp.GetRequiredKeyedService<OpenAIClient>("Audio");

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

        services.AddScoped<IExternalEscalationService, ExternalEscalationService>();

        services.AddScoped<IExternalEscalationOutcomePublisher, ExternalEscalationOutcomePublisher>();

        services.AddScoped<IInboundMessageBatchProcessor, InboundMessageBatchProcessor>();

        services.AddScoped<ITimedProcessScheduleProvider, SystemConfigurationTimedProcessScheduleProvider>();
        services.AddSingleton<ITimedProcessSchedulePolicy, TimedProcessSchedulePolicy>();
        services.AddScoped<ITimedProcessScheduler, TimedProcessScheduler>();

        services.AddScoped<ITimedProcess, PaymentLinkPollingProcess>();

        services.AddScoped<ITimedProcess, ExternalEscalationExpirationProcess>();

        services.AddScoped<ITimedProcess, ReservationAutomationProcess>();



        // -- AGENTIC ENGINE (Function Calling) -------------------------------------

        services.AddScoped<IChatClient>(sp =>

        {

            var textClient = sp.GetRequiredKeyedService<OpenAIClient>("Text");

            var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;

            var logger = sp.GetRequiredService<ILogger<AzureOpenAIChatClient>>();

            return new AzureOpenAIChatClient(textClient, textOptions.DeploymentName, logger);

        });



        services.AddScoped<IAgentConfigProvider, AgentConfigProvider>();



        // Hydrator: plugin model

        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelPhoneResolver>();

        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelEmailResolver>();

        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.EngagementResolver>();

        services.AddSingleton<IFactHydrator, FactHydrator>();

        services.AddSingleton<IFlowStageDetector, FlowStageDetector>();

        services.AddScoped<IConversationVerificationService, ConversationVerificationService>();

        services.AddScoped<IGuardEvaluator, GuardEvaluator>();

        services.AddScoped<IToolCapabilityGate, ToolCapabilityGate>();
services.AddSingleton<ITurnEventExtractor, NoOpTurnEventExtractor>();
services.AddScoped<IFlowRuntimeStateResolver, FlowRuntimeStateResolver>();
services.AddScoped<IFlowPolicyEngine, FlowPolicyEngine>();
services.AddScoped<IFlowRuntimeOrchestrator, FlowRuntimeOrchestrator>();

        services.AddScoped<IOperatingHoursTurnPolicy, OperatingHoursTurnPolicy>();

        services.AddScoped<IAgentTurnToolResolver, AgentTurnToolResolver>();

        services.AddScoped<IPromptComposer, AgentPromptComposer>();



        services.AddScoped<IAgentTemplateResolver, AgentTemplateResolver>();

        services.AddScoped<ITemplateRenderer, PromptTemplateRenderer>();

        services.AddScoped<IAgentTurnResponseComposer, AgentTurnResponseComposer>();



        services.AddScoped<IAgentTool, CheckAvailabilityTool>();

        services.AddScoped<IAgentTool, ResolvePricingTool>();

        services.AddScoped<IAgentTool, PrepareCheckoutTool>();

        services.AddScoped<IAgentTool, PrepareOrderCheckoutTool>();

        services.AddScoped<IAgentTool, CreateReservationTool>();

        services.AddScoped<IAgentTool, SuspendReservationTool>();

        services.AddScoped<IAgentTool, GetCustomerReservationsTool>();

        services.AddScoped<IAgentTool, ConfirmReservationAttendanceTool>();

        services.AddScoped<IAgentTool, ReschedulePaidReservationTool>();

        services.AddScoped<IAgentTool, ManageReservationTool>();

        services.AddScoped<IAgentTool, RequestReservationRescheduleTool>();

        services.AddScoped<IAgentTool, PrepareReservationChangeTool>();

        services.AddScoped<IAgentTool, ConfirmReservationChangeTool>();

        services.AddScoped<IAgentTool, VerifyPaymentTool>();

        services.AddScoped<IAgentTool, EscalateToHumanTool>();

        services.AddScoped<IAgentTool, GetServiceCatalogTool>();

services.AddScoped<IAgentTool, ResolveServiceSelectionTool>();

        services.AddScoped<IAgentTool, GetCompatibleAddOnsTool>();

        services.AddScoped<IAgentTool, GetServiceFulfillmentTool>();

        services.AddScoped<IAgentTool, SearchProductsTool>();

        services.AddScoped<IAgentTool, AddOrderItemTool>();

        services.AddScoped<IAgentTool, RemoveOrderItemTool>();

        services.AddScoped<IAgentTool, UpdateOrderItemQuantityTool>();

        services.AddScoped<IAgentTool, GetOrderDraftTool>();

        services.AddScoped<IAgentTool, CreateOrderTool>();

        services.AddScoped<IAgentTool, StartExternalInteractionTool>();

        services.AddScoped<IAgentTool, SearchOrderTool>();

        services.AddScoped<IAgentTool, AcceptOrderRequestTool>();

        services.AddScoped<IAgentTool, RejectOrderRequestTool>();

        services.AddScoped<IAgentTool, OperationsGetReservationsTool>();

        services.AddScoped<IAgentTool, OperationsBlockAvailabilityTool>();

        services.AddScoped<IAgentTool, OperationsRequestRescheduleTool>();

        services.AddScoped<IAgentTool, OperationsBusinessMetricsTool>();

        services.AddScoped<IAgentTool, OperationsCustomerHistoryTool>();

        services.AddScoped<IAgentTool, SetFactTool>();

        services.AddScoped<IAgentTool, ResetFlowContextTool>();

        services.AddScoped<IAgentTool, SendMessageSequenceTool>();



        services.AddScoped<AgentToolRegistry>();

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

            var config = sp.GetRequiredService<IConfiguration>();

            var connectionString = config["AzureWebJobsStorage"]

                ?? throw new InvalidOperationException("AzureWebJobsStorage debe estar configurado");

            return new BlobServiceClient(connectionString);

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
