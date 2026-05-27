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
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Infrastructure.LLM;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

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
    builder.AddFilter("MimosBabySpa.Application.Agents", LogLevel.Information);
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
services.AddScoped<MimosBabySpa.Domain.Repositories.IAgentRepository, AgentRepository>();

// ── Application Services ───────────────────────────────────────────────────────
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

// ── Supporting Infrastructure ──────────────────────────────────────────────────
services.AddMemoryCache();
services.AddScoped<ILocalizationService, LocalizationService>();
services.AddScoped<IConversationStateManager, ConversationStateManager>();
services.AddScoped<IConversationFactsService, ConversationFactsService>();
services.AddScoped<IConversationFactsService, ConversationFactsService>();
services.AddScoped<IReservationLifecycleService, ReservationLifecycleService>();
services.AddScoped<ICustomerReservationResolver, CustomerReservationResolver>();
services.AddScoped<IPaymentLifecycleService, PaymentLifecycleService>();
services.AddScoped<IReservationIntentBuilder, ReservationIntentBuilder>();
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
services.AddScoped<ISchedulingPolicyProvider, SchedulingPolicyProvider>();
services.AddScoped<IBookingPolicyProvider, BookingPolicyProvider>();
services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();

services.AddHttpClient();
services.AddHttpClient<GoogleCalendarService>(c => c.Timeout = TimeSpan.FromSeconds(30));
services.AddScoped<ICalendarService, GoogleCalendarService>();

// ── Business Rules Engine ──────────────────────────────────────────────────────
services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();

// ── Agentic Engine (Function Calling) ─────────────────────────────────────────
services.AddScoped<MimosBabySpa.Application.LLM.IChatClient>(sp =>
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

// ── Build ──────────────────────────────────────────────────────────────────────
var serviceProvider = services.BuildServiceProvider();

// ── Console UI ─────────────────────────────────────────────────────────────────
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine("  Mimos Baby Spa — Simulador Agentic Engine (FC)");
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("  Agente  : Mimi Bot (+573194823017)");
Console.WriteLine("  Escribe  'exit' para salir");
Console.WriteLine("  Escribe  'reset' para reiniciar la sesión");
Console.WriteLine();

// AgentId del Mimi Bot — resuelto directamente de la BD.
const string agentIdStr = "7105A9D5-D4E4-4BBA-9F3A-DBB34E0B1B86";
var agentId = Guid.Parse(agentIdStr);

// Simula el teléfono del cliente (clave de sesión)
const string userPhone = "+12345679770";

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
            input,
            userPhone);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Mimi: ");
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
        if (ex.InnerException is not null)
            Console.WriteLine($"  Causa: {ex.InnerException.Message}");
        Console.ResetColor();
        Console.WriteLine();
    }
}
