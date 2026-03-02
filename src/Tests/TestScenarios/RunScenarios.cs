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
using MimosBabySpa.Infrastructure.Services;
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
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Console.Services;

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
        services.AddScoped<IMessageService, MessageService>(); // ✅ Nuevo: Para historial conversacional
        
        // Integrations Config Provider + Release Link
        services.AddOptions();
        services.AddScoped<MimosBabySpa.Application.Configuration.IIntegrationsConfigProvider, MimosBabySpa.Infrastructure.Configuration.IntegrationsConfigProvider>();
        services.Configure<MimosBabySpa.Infrastructure.Configuration.ReleaseLinkSettings>(
            configuration.GetSection(MimosBabySpa.Infrastructure.Configuration.ReleaseLinkSettings.SectionName));
        
        // Infrastructure Services - Calendar (lee config desde BusinessConfiguration vía IIntegrationsConfigProvider)
        services.AddHttpClient();
        services.AddScoped<ICalendarService, MimosBabySpa.Infrastructure.Services.GoogleCalendarService>();

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

        // ✅ Memory Cache (para CachedBusinessContextProvider)
        services.AddMemoryCache();

        // ✅ Cached Business Context Provider
        services.AddScoped<CachedBusinessContextProvider>();

        // ✅ Prompt Provider
        services.AddScoped<IPromptProvider, SystemPromptProvider>();

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
        services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();
        services.AddScoped<IConversationStateUpdater, ConversationStateUpdater>();
        services.AddScoped<CheckAvailabilityToolHandler>();
        services.AddScoped<CreateReservationToolHandler>();
        
        // Tool Factory & Dispatcher
        services.AddScoped<IToolFactory, ToolFactory>();
        services.AddScoped<GenericToolDispatcher>();

        // Extraction Services
        services.AddScoped<JsonSchemaPromptBuilder>();
        services.AddScoped<IExtractionValidator, ExtractionValidator>();
        services.AddScoped<ISmartExtractionService, SmartExtractionService>();

        // Escalation y release
        services.AddScoped<IWhatsAppService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ConsoleWhatsAppService>>();
            return new ConsoleWhatsAppService(logger);
        });
        services.AddScoped<IEscalationNotifier, EscalationNotifier>();
        services.AddScoped<IEscalationConfigProvider, EscalationConfigProvider>();
        services.AddScoped<MimosBabySpa.Infrastructure.Services.AdminActionLinkService>();
        services.AddScoped<IAdminActionLinkService>(sp => sp.GetRequiredService<MimosBabySpa.Infrastructure.Services.AdminActionLinkService>());
        services.AddScoped<IReleaseLinkService>(sp => sp.GetRequiredService<MimosBabySpa.Infrastructure.Services.AdminActionLinkService>());
        services.AddScoped<IConversationReleaseService, ConversationReleaseService>();

        // Hybrid Transactional Orchestrator
        services.AddScoped<HybridTransactionalOrchestrator>();

        // Build service provider
        var serviceProvider = services.BuildServiceProvider();

        // Ejecutar pruebas automatizadas de conversaciones
        var orchestrator = serviceProvider.GetRequiredService<HybridTransactionalOrchestrator>();
        var stateManager = serviceProvider.GetRequiredService<IConversationStateManager>();
        var conversationService = serviceProvider.GetRequiredService<IConversationService>();
        
        // Determinar qué pruebas ejecutar según argumentos
        var testToRun = args.Length > 0 ? args[0].ToLower() : "all";
        
        if (testToRun == "all" || testToRun == "automated")
        {
            // Pruebas automatizadas existentes
            var logger1 = serviceProvider.GetRequiredService<ILogger<AutomatedConversationTests>>();
            var automatedTests = new AutomatedConversationTests(orchestrator, stateManager, conversationService, logger1);
            await automatedTests.RunAllTestsAsync();
            
            if (testToRun == "all")
            {
                Console.WriteLine("\n\n");
            }
        }

        if (testToRun == "all" || testToRun == "regression")
        {
            // Pruebas de regresión (conversaciones reales que expusieron bugs)
            var loggerReg = serviceProvider.GetRequiredService<ILogger<RegressionConversationTests>>();
            var regressionTests = new RegressionConversationTests(orchestrator, stateManager, conversationService, loggerReg);
            await regressionTests.RunAllTestsAsync();
            
            if (testToRun == "all")
            {
                Console.WriteLine("\n\n");
            }
        }
        
        if (testToRun == "all" || testToRun.StartsWith("behavior"))
        {
            // Pruebas de comportamiento conversacional
            var logger2 = serviceProvider.GetRequiredService<ILogger<ConversationalBehaviorTests>>();
            var behaviorTests = new ConversationalBehaviorTests(orchestrator, stateManager, conversationService, logger2);
            
            // Si se especifica un número de prueba específico (ej: "behavior:1" o "behavior1")
            var testNumber = ExtractTestNumber(testToRun);
            if (testNumber.HasValue)
            {
                await RunSingleBehaviorTestAsync(behaviorTests, testNumber.Value);
            }
            else
            {
                await behaviorTests.RunAllTestsAsync();
            }
        }
    }
    
    /// <summary>
    /// Ejecuta una prueba de comportamiento específica
    /// </summary>
    static async Task RunSingleBehaviorTestAsync(ConversationalBehaviorTests tests, int testNumber)
    {
        Console.WriteLine("================================================");
        Console.WriteLine($"  EJECUTANDO PRUEBA {testNumber}");
        Console.WriteLine("================================================");
        Console.WriteLine();
        
        var result = testNumber switch
        {
            1 => await tests.Test1_GreetingBehaviorAsync(),
            2 => await tests.Test2_AutomaticAvailabilityCheckAsync(),
            3 => await tests.Test3_NoFalsePromisesAsync(),
            4 => await tests.Test4_RealBackendTimeSlotsAsync(),
            5 => await tests.Test5_ImplicitReferenceInferenceAsync(),
            _ => throw new ArgumentException($"Número de prueba inválido: {testNumber}. Debe ser 1-5")
        };
        
        Console.WriteLine();
        Console.WriteLine("================================================");
        Console.WriteLine("  RESULTADO");
        Console.WriteLine("================================================");
        var icon = result.Success ? "✅" : "❌";
        Console.WriteLine($"{icon} {result.TestName}");
        if (!result.Success && !string.IsNullOrEmpty(result.Error))
        {
            Console.WriteLine($"   Error: {result.Error}");
        }
    }
    
    /// <summary>
    /// Extrae el número de prueba de un argumento como "behavior:1" o "behavior1"
    /// </summary>
    static int? ExtractTestNumber(string arg)
    {
        // Buscar patrones como "behavior:1", "behavior1", "1"
        var match = System.Text.RegularExpressions.Regex.Match(arg, @"(?:behavior:?|test)?(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
        {
            return number;
        }
        return null;
    }
}
