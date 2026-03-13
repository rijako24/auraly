using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Repositories;
using MimosBabySpa.Infrastructure.Services;
using MimosBabySpa.Infrastructure.Configuration;
using MimosBabySpa.Console.Services;
using Azure.Storage.Blobs;
using Azure.AI.OpenAI;
using System.Net.Http;

// HYBRID TRANSACTIONAL BRAIN - New Architecture
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.LLM.Extraction;
using Microsoft.Extensions.Options;

// Configurar servicios (usa directorio del ejecutable para encontrar appsettings; migrate-integrations lee Calendar y Wompi)
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appSettings.json", optional: true, reloadOnChange: true)
    .Build();

// Migración única: copiar Calendar y Wompi de appsettings a BusinessConfiguration.Integrations
if (args is ["migrate-integrations"])
{
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection no configurado");
    await MimosBabySpa.Console.IntegrationsDataMigration.RunAsync(configuration, connectionString);
    return;
}

var services = new ServiceCollection();

// Registrar IConfiguration
services.AddSingleton<IConfiguration>(configuration);

// Logging
services.AddLogging(builder =>
{
    builder.AddConfiguration(configuration.GetSection("Logging"));
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning);
    // Desactivar completamente los logs de Entity Framework
    builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
    builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
    builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Transaction", LogLevel.None);
    builder.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.None);
    builder.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.None);
    // Solo mostrar errores y advertencias de nuestra aplicación
    builder.AddFilter("MimosBabySpa", LogLevel.Warning);
});

// Database
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

// Repositories
services.AddScoped<IUnitOfWork, UnitOfWork>();
services.AddScoped<IConversationRepository, ConversationRepository>();
services.AddScoped<IConversationStateRepository, ConversationStateRepository>();
services.AddScoped<IMessageRepository, MessageRepository>();
services.AddScoped<ILeadRepository, LeadRepository>();
services.AddScoped<IReservationRepository, ReservationRepository>();
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

// State Management (debe ser Scoped para usar IConversationStateRepository)
services.AddScoped<IConversationStateManager, ConversationStateManager>();

// Business Rules Engine
services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();

// ✅ NEW: Cached Business Context Provider (elimina cargas redundantes + caché)
services.AddMemoryCache();
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

// Tool Handlers (Domain-Agnostic)
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

// Escalation y release (handover a humano)
services.AddScoped<IEscalationNotifier, EscalationNotifier>();
services.AddScoped<IEscalationConfigProvider, EscalationConfigProvider>();
services.AddScoped<AdminActionLinkService>();
services.AddScoped<IAdminActionLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
services.AddScoped<IReleaseLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
services.AddScoped<IConversationReleaseService, ConversationReleaseService>();

// Hybrid Transactional Orchestrator
services.AddScoped<HybridTransactionalOrchestrator>();

// Infrastructure Services - WhatsApp (Mock para consola)
// Usamos ConsoleWhatsAppService que muestra las respuestas en consola en lugar de enviarlas por WhatsApp
services.AddScoped<IWhatsAppService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ConsoleWhatsAppService>>();
    return new ConsoleWhatsAppService(logger);
});


// WhatsAppMessageProcessorService (usa HybridTransactionalOrchestrator)
services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();

// Infrastructure Services - Blob Storage (Mock para consola)
// Usamos ConsoleBlobStorageService que retorna valores vacíos ya que no es necesario para probar OpenAI
services.AddScoped<IBlobStorageService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ConsoleBlobStorageService>>();
    return new ConsoleBlobStorageService(logger);
});

// Integrations Config Provider (Google Calendar, Wompi) — fuente única desde BusinessConfiguration
services.AddScoped<IIntegrationsConfigProvider, IntegrationsConfigProvider>();

// Release Link (URL firmada; sin config = no se genera link en notificaciones)
services.Configure<ReleaseLinkSettings>(
    configuration.GetSection(ReleaseLinkSettings.SectionName));

// Payment Link Service (Wompi; sin config = no genera links)
services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();

// Infrastructure Services - Calendar
services.AddHttpClient();
services.AddHttpClient<GoogleCalendarService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
services.AddScoped<ICalendarService, GoogleCalendarService>();

// Construir el service provider
var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var processor = serviceProvider.GetRequiredService<IWhatsAppMessageProcessorService>();

// Interfaz de consola
Console.WriteLine("================================================");
Console.WriteLine("  Mimos Baby Spa - Simulador de WhatsApp");
Console.WriteLine("================================================");
Console.WriteLine();
Console.WriteLine("Escribe mensajes como si fueras un cliente de WhatsApp.");
Console.WriteLine("Escribe 'exit' o 'quit' para salir.");
Console.WriteLine();

// Número de teléfono simulado (puedes cambiarlo)
var userNumber = "+12345679514";
var customerName = "Bill";

while (true)
{
    Console.Write("Tú: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) || 
        input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("salir", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("¡Hasta luego!");
        break;
    }

    try
    {
        // Usar el BusinessId por defecto que existe en la base de datos (creado en la migración)
        var businessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        
        // Procesar el mensaje como si viniera de WhatsApp
        await processor.ProcessIncomingMessageAsync(businessId, userNumber, input, customerName);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error procesando mensaje");
        Console.WriteLine($"✗ Error: {ex.Message}");
        Console.WriteLine();
    }
}
