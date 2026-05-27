using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Repositories;
using MimosBabySpa.Infrastructure.Services;
using MimosBabySpa.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;

using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.StateManagement;

// Agentic Engine (Function Calling)
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Time;
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

        // Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IConversationStateRepository, ConversationStateRepository>();
        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();

        // Application Services
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IConversationLifecycleService, ConversationLifecycleService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<ILeadService, LeadService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IBusinessIdentificationService, BusinessIdentificationService>();
        services.AddScoped<IBusinessConfigurationService, BusinessConfigurationService>();
        services.AddScoped<IWhatsAppWebhookParserService, WhatsAppWebhookParserService>();
        services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<ServiceNameResolver>();
        services.AddScoped<ReservationPricingResolver>();
        services.AddScoped<ReservationCheckoutPricing>();
        services.AddScoped<IReservationCheckoutPricing>(sp =>
            sp.GetRequiredService<ReservationCheckoutPricing>());
        services.AddScoped<IBusinessClock, BusinessClock>();
        services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();
        services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();
        services.AddScoped<IAddOnCatalogService, AddOnCatalogService>();

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

        // AI Service (solo transcripción de audio con Whisper)
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
        services.AddScoped<PaymentConfirmationNotifier>();

        services.AddScoped<IConversationFactsService, ConversationFactsService>();
        services.AddScoped<IReservationLifecycleService, ReservationLifecycleService>();
        services.AddScoped<ICustomerReservationResolver, CustomerReservationResolver>();
        services.AddScoped<IPaymentLifecycleService, PaymentLifecycleService>();
        services.AddScoped<IReservationIntentBuilder, ReservationIntentBuilder>();

        // Webhook signature validation (Wompi)
        services.AddSingleton<IWompiWebhookSignatureValidator, WompiWebhookSignatureValidator>();

        // Escalation y release (handover a humano)
        services.AddScoped<IEscalationNotifier, EscalationNotifier>();
        services.AddScoped<IEscalationConfigProvider, EscalationConfigProvider>();
        services.AddScoped<AdminActionLinkService>();
        services.AddScoped<IAdminActionLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
        services.AddScoped<IReleaseLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
        services.AddScoped<IConversationReleaseService, ConversationReleaseService>();

        // ── AGENTIC ENGINE (Function Calling) ─────────────────────────────────────
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
        services.AddScoped<IPromptComposer, AgentPromptComposer>();

        services.AddScoped<IAgentTemplateResolver, AgentTemplateResolver>();
        services.AddScoped<ITemplateRenderer, PromptTemplateRenderer>();
        services.AddScoped<IAgentTurnResponseComposer, AgentTurnResponseComposer>();

        services.AddScoped<IAgentTool, CheckAvailabilityTool>();
        services.AddScoped<IAgentTool, ResolvePricingTool>();
        services.AddScoped<IAgentTool, PrepareCheckoutTool>();
        services.AddScoped<IAgentTool, CreateReservationTool>();
        services.AddScoped<IAgentTool, AssignPaidSlotTool>();
        services.AddScoped<IAgentTool, RescheduleReservationTool>();
        services.AddScoped<IAgentTool, SuspendReservationTool>();
        services.AddScoped<IAgentTool, GeneratePaymentLinkTool>();
        services.AddScoped<IAgentTool, VerifyPaymentTool>();
        services.AddScoped<IAgentTool, EscalateToHumanTool>();
        services.AddScoped<IAgentTool, GetServiceCatalogTool>();
        services.AddScoped<IAgentTool, SetFactTool>();

        services.AddScoped<AgentToolRegistry>();
        services.AddScoped<IAgentConversationService, AgentConversationService>();

        // WhatsAppMessageProcessorService (usa AgentConversationService)
        services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();

        // ── Infrastructure Services - WhatsApp ─────────────────────────────────────
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
        services.AddScoped<IBookingPolicyProvider, BookingPolicyProvider>();

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
