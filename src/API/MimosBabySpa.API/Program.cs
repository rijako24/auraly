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
        
        // Infrastructure Services - OpenAI (debe estar antes del LLM Adapter)
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var endpoint = config["OpenAI:Endpoint"] ?? throw new InvalidOperationException("OpenAI:Endpoint no configurado");
            var apiKey = config["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey no configurado");
            
            return new OpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
        });
        
        // AI Service (para transcripción de audio)
        services.AddScoped<IAIService>(sp =>
        {
            var openAIClient = sp.GetRequiredService<OpenAIClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var textDeploymentName = config["OpenAI:TextDeploymentName"] ?? "gpt-4o-mini";
            var audioDeploymentName = config["OpenAI:AudioDeploymentName"] ?? "whisper";
            var systemPromptProvider = sp.GetRequiredService<IPromptProvider>();
            var cachedContextProvider = sp.GetRequiredService<CachedBusinessContextProvider>();
            var logger = sp.GetRequiredService<ILogger<AIService>>();
            
            return new AIService(openAIClient, textDeploymentName, audioDeploymentName, systemPromptProvider, cachedContextProvider, logger);
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
        
        // LLM Adapter Layer
        services.AddScoped<ILLMAdapter>(sp =>
        {
            var openAIClient = sp.GetRequiredService<OpenAIClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var deploymentName = config["OpenAI:TextDeploymentName"] ?? "gpt-4o-mini";
            var logger = sp.GetRequiredService<ILogger<AzureOpenAIAdapter>>();
            
            return new AzureOpenAIAdapter(openAIClient, deploymentName, logger);
        });
        
        // Tool Handlers (Domain-Agnostic)
        services.AddScoped<IConversationStateUpdater, ConversationStateUpdater>();
        services.AddScoped<UpdateConversationStateToolHandler>();
        services.AddScoped<CheckAvailabilityToolHandler>();
        services.AddScoped<CreateReservationToolHandler>();
        
        // Tool Factory & Dispatcher
        services.AddScoped<IToolFactory, ToolFactory>();
        services.AddScoped<GenericToolDispatcher>();
        
        // Extraction Services
        services.AddScoped<JsonSchemaPromptBuilder>(); // ✅ Refactorizado para usar LoadedBusinessContext
        services.AddScoped<IExtractionValidator, ExtractionValidator>();
        services.AddScoped<IFallbackExtractor, FallbackExtractor>();
        services.AddScoped<ISmartExtractionService, SmartExtractionService>(); // ✅ Refactorizado
        
        // Hybrid Transactional Orchestrator
        services.AddScoped<HybridTransactionalOrchestrator>();
        
        // WhatsAppMessageProcessorService (usa HybridTransactionalOrchestrator)
        services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();

        // Infrastructure Services - WhatsApp
        services.AddHttpClient();
        services.AddScoped<IWhatsAppService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            var config = sp.GetRequiredService<IConfiguration>();
            var phoneNumberId = config["WhatsApp:PhoneNumberId"] ?? throw new InvalidOperationException("WhatsApp:PhoneNumberId no configurado");
            var accessToken = config["WhatsApp:AccessToken"] ?? throw new InvalidOperationException("WhatsApp:AccessToken no configurado");
            var logger = sp.GetRequiredService<ILogger<WhatsAppService>>();
            
            return new WhatsAppService(httpClient, phoneNumberId, accessToken, logger);
        });



        // Infrastructure Services - Blob Storage
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config["BlobStorage:ConnectionString"] ?? throw new InvalidOperationException("BlobStorage:ConnectionString no configurado");
            
            return new BlobServiceClient(connectionString);
        });

        services.AddScoped<IBlobStorageService>(sp =>
        {
            var client = sp.GetRequiredService<BlobServiceClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var containerName = config["BlobStorage:ContainerName"] ?? "planes-images";
            
            return new BlobStorageService(client, containerName, sp.GetRequiredService<ILogger<BlobStorageService>>());
        });

        // Calendar Configuration (Options Pattern)
        services.Configure<CalendarSettings>(
            configuration.GetSection(CalendarSettings.SectionName));

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
