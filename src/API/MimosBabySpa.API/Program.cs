using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Repositories;
using MimosBabySpa.Infrastructure.Services;
using MimosBabySpa.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Azure.AI.OpenAI;
using System.Net.Http;
using Microsoft.Extensions.Options;

// HYBRID TRANSACTIONAL BRAIN - New Architecture
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Application.Prompts;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Database
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // ✅ Memory Cache (para CachedBusinessContextProvider)
        services.AddMemoryCache();

        // Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IConversationStateRepository, ConversationStateRepository>();
        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();

        // Application Services
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<ILeadService, LeadService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IBusinessIdentificationService, BusinessIdentificationService>();
        services.AddScoped<IBusinessConfigurationService, BusinessConfigurationService>();
        services.AddScoped<IWhatsAppWebhookParserService, WhatsAppWebhookParserService>();
        
        // Services necesarios para Tools
        services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        
        // ========================================
        // HYBRID TRANSACTIONAL BRAIN ARCHITECTURE
        // ========================================
        
        // Infrastructure Services - OpenAI (Options como fuente única de configuración)
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

        // AI Service (chat + transcripción de audio con clientes separados)
        services.AddScoped<IAIService>(sp =>
        {
            var textClient = sp.GetRequiredKeyedService<OpenAIClient>("Text");
            var audioClient = sp.GetRequiredKeyedService<OpenAIClient>("Audio");
            var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;
            var audioOptions = sp.GetRequiredService<IOptions<OpenAIAudioModelOptions>>().Value;
            var systemPromptProvider = sp.GetRequiredService<IPromptProvider>();
            var cachedContextProvider = sp.GetRequiredService<CachedBusinessContextProvider>();
            var logger = sp.GetRequiredService<ILogger<AIService>>();

            return new AIService(textClient, audioClient, textOptions.DeploymentName, audioOptions.DeploymentName, systemPromptProvider, cachedContextProvider, logger);
        });
        
        // Flow Engine (Cerebro Determinístico)
        services.AddSingleton<IFlowEngine, FlowEngine>();
        
        // State Management (necesita IConversationStateRepository e IConversationService)
        services.AddScoped<IConversationStateManager, ConversationStateManager>();
        
        // Business Rules Engine
        services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();
        
        // ✅ NEW: Cached Business Context Provider (elimina cargas redundantes + caché)
        services.AddScoped<CachedBusinessContextProvider>();
        
        // ✅ NEW: Prompt Providers (prompts organizados y modulares)
        services.AddScoped<IPromptProvider, SystemPromptProvider>();
        
        // ✅ NEW: Localization Service (i18n básico)
        services.AddSingleton<ILocalizationService, LocalizationService>();
        
        // LLM Adapter Layer (usa cliente de texto)
        services.AddScoped<ILLMAdapter>(sp =>
        {
            var textClient = sp.GetRequiredKeyedService<OpenAIClient>("Text");
            var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<AzureOpenAIAdapter>>();

            return new AzureOpenAIAdapter(textClient, textOptions.DeploymentName, logger);
        });
        
        // Payment Link Service (Wompi)
        services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();

        // Payment Confirmation Handler (Webhook)
        services.AddScoped<IPaymentConfirmationHandler, PaymentConfirmationHandler>();

        // Payment Confirmation Messages (configurables por negocio)
        services.AddScoped<IMediaUrlResolver, BlobMediaUrlResolver>();
        services.AddScoped<PaymentConfirmationNotifier>();

        // Webhook signature validation (Wompi)
        services.AddSingleton<IWompiWebhookSignatureValidator, WompiWebhookSignatureValidator>();

        // Tool Handlers (Domain-Agnostic)
        services.AddScoped<IConversationStateUpdater, ConversationStateUpdater>();
        services.AddScoped<CheckAvailabilityToolHandler>();
        services.AddScoped<CreateReservationToolHandler>();
        
        // Tool Factory & Dispatcher
        services.AddScoped<IToolFactory, ToolFactory>();
        services.AddScoped<GenericToolDispatcher>();
        
        // Extraction Services
        services.AddScoped<JsonSchemaPromptBuilder>(); // ✅ Refactorizado para usar LoadedBusinessContext
        services.AddScoped<IExtractionValidator, ExtractionValidator>();
        services.AddScoped<ISmartExtractionService, SmartExtractionService>();

        // Escalation y release (handover a humano)
        services.AddScoped<IEscalationNotifier, EscalationNotifier>();
        services.AddScoped<IEscalationConfigProvider, EscalationConfigProvider>();
        services.AddScoped<AdminActionLinkService>();
        services.AddScoped<IAdminActionLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
        services.AddScoped<IReleaseLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
        services.AddScoped<IConversationReleaseService, ConversationReleaseService>();

        // Hybrid Transactional Orchestrator
        services.AddScoped<HybridTransactionalOrchestrator>();
        
        // WhatsAppMessageProcessorService (usa HybridTransactionalOrchestrator)
        services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();

        // Infrastructure Services - WhatsApp (credenciales desde BusinessWhatsAppNumbers)
        services.AddHttpClient();
        services.Configure<WhatsAppWebhookOptions>(
            configuration.GetSection(WhatsAppWebhookOptions.SectionName));
        services.AddScoped<IWhatsAppCredentialResolver, WhatsAppCredentialResolver>();
        services.AddScoped<IWhatsAppService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var resolver = sp.GetRequiredService<IWhatsAppCredentialResolver>();
            var logger = sp.GetRequiredService<ILogger<WhatsAppService>>();
            var webhookOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WhatsAppWebhookOptions>>();
            return new WhatsAppService(httpClient, resolver, logger, webhookOptions);
        });



        // Infrastructure Services - Blob Storage (usa AzureWebJobsStorage)
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config["AzureWebJobsStorage"] ?? throw new InvalidOperationException("AzureWebJobsStorage debe estar configurado");
            
            return new BlobServiceClient(connectionString);
        });

        services.AddScoped<IBlobStorageService>(sp =>
        {
            var client = sp.GetRequiredService<BlobServiceClient>();
            return new BlobStorageService(client, sp.GetRequiredService<ILogger<BlobStorageService>>());
        });

        // Integrations Config Provider (Google Calendar, Wompi) — fuente única desde BusinessConfiguration
        services.AddScoped<IIntegrationsConfigProvider, IntegrationsConfigProvider>();

        // Release Link (URL firmada para devolver conversación al bot)
        services.Configure<ReleaseLinkSettings>(
            configuration.GetSection(ReleaseLinkSettings.SectionName));

        // Infrastructure Services - Calendar
        services.AddHttpClient<GoogleCalendarService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ICalendarService, GoogleCalendarService>();

        // Application Insights (opcional)
        // services.AddApplicationInsightsTelemetryWorkerService();
    })
    .Build();

host.Run();
