using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Repositories;
using MimosBabySpa.Infrastructure.Services;
using MimosBabySpa.Infrastructure.Configuration;
using MimosBabySpa.Console.Services;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;

// Agentic Engine
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Infrastructure.LLM;

// Generic Flow Engine (legacy — kept until full migration)
using MimosBabySpa.Application.GenericFlow;
using MimosBabySpa.Application.GenericFlow.Actions;
using MimosBabySpa.Application.GenericFlow.Handlers;
using MimosBabySpa.Application.GenericFlow.Services;

// Legacy (kept for migrate-integrations command only)
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.LLM.Extraction;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appSettings.json", optional: true, reloadOnChange: true)
    .Build();

if (args is ["migrate-integrations"])
{
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection no configurado");
    await MimosBabySpa.Console.IntegrationsDataMigration.RunAsync(configuration, connectionString);
    return;
}

var services = new ServiceCollection();

services.AddSingleton<IConfiguration>(configuration);

services.AddLogging(builder =>
{
    builder.AddConfiguration(configuration.GetSection("Logging"));
    builder.AddConsoleFormatter<MimosBabySpa.Console.CleanConsoleFormatter, ConsoleFormatterOptions>();
    builder.AddConsole(options => options.FormatterName = MimosBabySpa.Console.CleanConsoleFormatter.FormatterName);
    builder.SetMinimumLevel(LogLevel.Warning);
    builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
    builder.AddFilter("MimosBabySpa", LogLevel.Warning);
    // Flow extraction + intentions for easier error routing
    builder.AddFilter("MimosBabySpa.Application.GenericFlow", LogLevel.Information);
});

// Database
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

// ── Core Repositories ──────────────────────────────────────────────────────────
services.AddScoped<IUnitOfWork, UnitOfWork>();
services.AddScoped<IConversationRepository, ConversationRepository>();
services.AddScoped<IConversationStateRepository, ConversationStateRepository>();
services.AddScoped<IMessageRepository, MessageRepository>();
services.AddScoped<ILeadRepository, LeadRepository>();
services.AddScoped<IReservationRepository, ReservationRepository>();
services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();

// ── Generic Flow Repositories ──────────────────────────────────────────────────
services.AddScoped<MimosBabySpa.Domain.Repositories.IAgentRepository, AgentRepository>();
services.AddScoped<MimosBabySpa.Domain.Repositories.IFlowDefinitionRepository, FlowDefinitionRepository>();
services.AddScoped<MimosBabySpa.Domain.Repositories.IFlowExecutionStateRepository, FlowExecutionStateRepository>();
services.AddScoped<MimosBabySpa.Domain.Repositories.IKnowledgeSourceRepository, KnowledgeSourceRepository>();

// ── Application Services ───────────────────────────────────────────────────────
services.AddScoped<IConversationService, ConversationService>();
services.AddScoped<IMessageService, MessageService>();
services.AddScoped<ILeadService, LeadService>();
services.AddScoped<IReservationService, ReservationService>();
services.AddScoped<IBusinessIdentificationService, BusinessIdentificationService>();
services.AddScoped<IBusinessConfigurationService, BusinessConfigurationService>();
services.AddScoped<IWhatsAppWebhookParserService, WhatsAppWebhookParserService>();
services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
services.AddScoped<IAvailabilityService, AvailabilityService>();

// ── OpenAI Clients ─────────────────────────────────────────────────────────────
services.Configure<OpenAITextModelOptions>(configuration.GetSection(OpenAITextModelOptions.SectionName));
services.Configure<OpenAIAudioModelOptions>(configuration.GetSection(OpenAIAudioModelOptions.SectionName));

services.AddKeyedSingleton<OpenAIClient>("Text", (sp, _) =>
{
    var opts = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;
    if (string.IsNullOrEmpty(opts.Endpoint) || string.IsNullOrEmpty(opts.ApiKey))
        throw new InvalidOperationException("OpenAI:TextModel:Endpoint y ApiKey deben estar configurados");
    return new OpenAIClient(new Uri(opts.Endpoint), new Azure.AzureKeyCredential(opts.ApiKey));
});

services.AddKeyedSingleton<OpenAIClient>("Audio", (sp, _) =>
{
    var opts = sp.GetRequiredService<IOptions<OpenAIAudioModelOptions>>().Value;
    if (string.IsNullOrEmpty(opts.Endpoint) || string.IsNullOrEmpty(opts.ApiKey))
        throw new InvalidOperationException("OpenAI:AudioModel:Endpoint y ApiKey deben estar configurados");
    return new OpenAIClient(new Uri(opts.Endpoint), new Azure.AzureKeyCredential(opts.ApiKey));
});

// LLM Adapter (shared by both legacy and generic engine)
services.AddScoped<ILLMAdapter>(sp =>
{
    var textClient = sp.GetRequiredKeyedService<OpenAIClient>("Text");
    var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<AzureOpenAIAdapter>>();
    return new AzureOpenAIAdapter(textClient, textOptions.DeploymentName, logger);
});

// ── Generic Flow Engine ────────────────────────────────────────────────────────

services.AddScoped<TemplateResolver>();
services.AddScoped<KnowledgeSourceRenderer>();
services.AddScoped<FlowPromptBuilder>();
services.AddScoped<FlowIntentionDetector>();
services.AddScoped<FlowExtractionService>();
services.AddScoped<FlowStateManager>();
services.AddScoped<ServiceNameResolver>();
services.AddScoped<ReservationPricingResolver>();
services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();

// Node Handlers
services.AddScoped<INodeHandler, StartNodeHandler>();
services.AddScoped<INodeHandler, EndNodeHandler>();
services.AddScoped<INodeHandler, CollectFieldsNodeHandler>();
services.AddScoped<INodeHandler, ActionNodeHandler>();
services.AddScoped<INodeHandler, LLMClassifyNodeHandler>();
services.AddScoped<INodeHandler, IntentionRouterNodeHandler>();
services.AddScoped<INodeHandler, GenerateResponseNodeHandler>();
services.AddScoped<INodeHandler, WaitForEventNodeHandler>();
services.AddScoped<INodeHandler, EscalateNodeHandler>();

// Flow Actions
services.AddScoped<IFlowAction, CheckAvailabilityAction>();
services.AddScoped<IFlowAction, ResolvePricingAction>();
services.AddScoped<IFlowAction, CreateReservationAction>();
services.AddScoped<IFlowAction, GeneratePaymentLinkAction>();
services.AddScoped<IFlowAction, VerifyPaymentAction>();
services.AddScoped<IFlowAction, SetupRescheduleAction>();
services.AddScoped<IFlowAction, RescheduleAction>();
services.AddScoped<IFlowAction, SuspendAction>();

services.AddScoped<IFlowOrchestrationService, FlowOrchestrationService>();

// ── Supporting infrastructure used by actions ──────────────────────────────────
services.AddMemoryCache();
services.AddScoped<CachedBusinessContextProvider>();
services.AddScoped<IPromptProvider, SystemPromptProvider>();
services.AddScoped<ILocalizationService, LocalizationService>();
services.AddScoped<IConversationStateManager, ConversationStateManager>();
services.AddScoped<IConversationStateUpdater, ConversationStateUpdater>();
services.AddScoped<IEscalationNotifier, EscalationNotifier>();
services.AddScoped<IEscalationConfigProvider, EscalationConfigProvider>();
services.AddScoped<AdminActionLinkService>();
services.AddScoped<IAdminActionLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
services.AddScoped<IReleaseLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
services.AddScoped<IConversationReleaseService, ConversationReleaseService>();
services.Configure<ReleaseLinkSettings>(configuration.GetSection(ReleaseLinkSettings.SectionName));

services.AddScoped<IWhatsAppService>(sp =>
    new ConsoleWhatsAppService(sp.GetRequiredService<ILogger<ConsoleWhatsAppService>>()));

services.AddScoped<IBlobStorageService>(sp =>
    new ConsoleBlobStorageService(sp.GetRequiredService<ILogger<ConsoleBlobStorageService>>()));

services.AddScoped<IIntegrationsConfigProvider, IntegrationsConfigProvider>();
services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();

services.AddHttpClient();
services.AddHttpClient<GoogleCalendarService>(c => c.Timeout = TimeSpan.FromSeconds(30));
services.AddScoped<ICalendarService, GoogleCalendarService>();

services.AddScoped<JsonSchemaPromptBuilder>();
services.AddScoped<IExtractionValidator, ExtractionValidator>();
services.AddScoped<ISmartExtractionService, SmartExtractionService>();

// ── Business Rules Engine ──────────────────────────────────────────────────────
services.AddScoped<MimosBabySpa.Application.BusinessRules.IBusinessRuleEngine,
    MimosBabySpa.Application.BusinessRules.BusinessRuleEngine>();

// ── Agentic Engine (Function Calling) ─────────────────────────────────────────
services.AddScoped<MimosBabySpa.Application.LLM.IChatClient>(sp =>
{
    var textClient = sp.GetRequiredKeyedService<OpenAIClient>("Text");
    var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<AzureOpenAIChatClient>>();
    return new AzureOpenAIChatClient(textClient, textOptions.DeploymentName, logger);
});

services.AddScoped<IAgentConfigProvider, AgentConfigProvider>();

services.AddScoped<IAgentTool, CheckAvailabilityTool>();
services.AddScoped<IAgentTool, ResolvePricingTool>();
services.AddScoped<IAgentTool, CreateReservationTool>();
services.AddScoped<IAgentTool, RescheduleReservationTool>();
services.AddScoped<IAgentTool, SuspendReservationTool>();
services.AddScoped<IAgentTool, GeneratePaymentLinkTool>();
services.AddScoped<IAgentTool, VerifyPaymentTool>();
services.AddScoped<IAgentTool, EscalateToHumanTool>();
services.AddScoped<IAgentTool, GetServiceCatalogTool>();

services.AddScoped<AgentToolRegistry>();
services.AddScoped<IAgentConversationService, AgentConversationService>();

// ── Build ──────────────────────────────────────────────────────────────────────
var serviceProvider = services.BuildServiceProvider();

// ── Console UI ─────────────────────────────────────────────────────────────────
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine("  Mimos Baby Spa — Simulador Agentic Engine (FC)");
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("  Agente  : Mimo Bot (+573194823017)");
Console.WriteLine("  Escribe  'exit' para salir");
Console.WriteLine("  Escribe  'reset' para reiniciar la sesión");
Console.WriteLine();

// AgentId del Mimo Bot — resuelto directamente de la BD (BusinessWhatsAppNumbers.AgentId).
// En producción este valor viene de BusinessIdentificationService.IdentifyBusinessAsync().
const string agentIdStr = "7105A9D5-D4E4-4BBA-9F3A-DBB34E0B1B86";
var agentId = Guid.Parse(agentIdStr);

// Simula el teléfono del cliente (clave de sesión)
const string userPhone = "+12345679703";

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Tú: ");
    Console.ResetColor();

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

    if (input.Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[sesión reiniciada — escribe tu siguiente mensaje]");
        Console.ResetColor();
        Console.WriteLine();
        continue;
    }

    try
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
        var agentService = scope.ServiceProvider.GetRequiredService<IAgentConversationService>();

        var agentEntity = await agentRepo.GetByIdAsync(agentId);
        if (agentEntity == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Agente {agentId} no encontrado en la base de datos.");
            Console.ResetColor();
            Console.WriteLine();
            continue;
        }

        var conversation = await conversationService.GetOrCreateConversationAsync(
            agentEntity.BusinessId, userPhone, customerName: null);

        var result = await agentService.ProcessMessageAsync(
            agentId,
            conversation.ConversationId,
            input);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Mimo: ");
        Console.ResetColor();

        if (!string.IsNullOrWhiteSpace(result.Response))
            Console.WriteLine(result.Response);
        else if (!result.Success)
            Console.WriteLine($"[error: {result.ErrorMessage}]");
        else
            Console.WriteLine("[sin respuesta]");

        Console.WriteLine();

        if (result.EscalatedToHuman)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ⚠  Conversación transferida a agente humano.");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ Error: {ex.Message}");
        Console.ResetColor();
        Console.WriteLine();
    }
}
