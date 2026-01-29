using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Repositories;
using Azure.AI.OpenAI;
using MimosBabySpa.Tests.TestScenarios;
using ConversationStateRepository = MimosBabySpa.Infrastructure.Repositories.ConversationStateRepository;

// HYBRID TRANSACTIONAL BRAIN - New Architecture
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.LLM.Extraction;

namespace MimosBabySpa.Tests.TestScenarios;

/// <summary>
/// Programa para ejecutar escenarios de prueba de Function Calling.
/// </summary>
class RunScenarios
{
    static async Task Main(string[] args)
    {
        // Configuración
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Configurar servicios
        var services = new ServiceCollection();
        
        // Registrar configuración
        services.AddSingleton<IConfiguration>(configuration);
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        // Database
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IBusinessConfigurationRepository, BusinessConfigurationRepository>();
        services.AddScoped<IConversationStateRepository, ConversationStateRepository>();

        // Services
        services.AddScoped<IBusinessConfigurationService, BusinessConfigurationService>();
        services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IReservationService, ReservationService>();
        
        // Calendar Configuration (Options Pattern)
        services.Configure<MimosBabySpa.Infrastructure.Configuration.CalendarSettings>(
            configuration.GetSection(MimosBabySpa.Infrastructure.Configuration.CalendarSettings.SectionName));
        
        // Infrastructure Services - Calendar (usar HttpClient simple para pruebas)
        services.AddHttpClient();
        services.AddScoped<ICalendarService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MimosBabySpa.Infrastructure.Configuration.CalendarSettings>>();
            var logger = sp.GetRequiredService<ILogger<MimosBabySpa.Infrastructure.Services.GoogleCalendarService>>();
            return new MimosBabySpa.Infrastructure.Services.GoogleCalendarService(httpClient, options, logger);
        });

        // ========================================
        // HYBRID TRANSACTIONAL BRAIN ARCHITECTURE
        // ========================================

        // Infrastructure Services - OpenAI
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var endpoint = config["OpenAI:Endpoint"] ?? throw new InvalidOperationException("OpenAI:Endpoint no configurado");
            var apiKey = config["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey no configurado");
            return new OpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
        });

        // Flow Engine (Cerebro Determinístico)
        services.AddSingleton<IFlowEngine, FlowEngine>();

        // State Management (necesita IConversationStateRepository e IConversationService)
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IConversationStateManager, ConversationStateManager>();

        // Business Rules Engine
        services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();

        // Business Configuration Provider
        services.AddScoped<IBusinessConfigurationProvider, BusinessConfigurationProvider>();

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
        services.AddScoped<UpdateConversationStateToolHandler>();
        services.AddScoped<CheckAvailabilityToolHandler>();
        services.AddScoped<CreateReservationToolHandler>();
        
        // Tool Factory & Dispatcher
        services.AddScoped<IToolFactory, ToolFactory>();
        services.AddScoped<GenericToolDispatcher>();

        // Extraction Services
        services.AddScoped<JsonSchemaPromptBuilder>();
        services.AddScoped<IExtractionValidator, ExtractionValidator>();
        services.AddScoped<IFallbackExtractor, FallbackExtractor>();
        services.AddScoped<ISmartExtractionService, SmartExtractionService>();

        // Hybrid Transactional Orchestrator
        services.AddScoped<HybridTransactionalOrchestrator>();

        // Build service provider
        var serviceProvider = services.BuildServiceProvider();

        // Ejecutar pruebas automatizadas de conversaciones
        var orchestrator = serviceProvider.GetRequiredService<HybridTransactionalOrchestrator>();
        var stateManager = serviceProvider.GetRequiredService<IConversationStateManager>();
        var conversationService = serviceProvider.GetRequiredService<IConversationService>();
        var logger = serviceProvider.GetRequiredService<ILogger<AutomatedConversationTests>>();
        
        var automatedTests = new AutomatedConversationTests(orchestrator, stateManager, conversationService, logger);
        await automatedTests.RunAllTestsAsync();
    }
}
