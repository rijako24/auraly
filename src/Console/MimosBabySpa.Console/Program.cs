using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Commerce;
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
using MimosBabySpa.Application.Billing;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Infrastructure.LLM;
using MimosBabySpa.Infrastructure.Commerce;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appSettings.json", optional: true, reloadOnChange: true)
    .Build();

if (args is ["backfill-customer-memory"])
{
    var backfillServices = new ServiceCollection();
    backfillServices.AddSingleton<IConfiguration>(configuration);
    backfillServices.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    backfillServices.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
    backfillServices.AddMemoryCache();
    backfillServices.AddScoped<IUnitOfWork, UnitOfWork>();
    backfillServices.AddScoped<IAgentRepository, AgentRepository>();
    backfillServices.AddScoped<IAgentConfigProvider, AgentConfigProvider>();
    backfillServices.AddScoped<ICustomerMemoryService, CustomerMemoryService>();
    backfillServices.AddScoped<ICustomerMemoryBackfillService, CustomerMemoryBackfillService>();

    await using var backfillProvider = backfillServices.BuildServiceProvider();
    var backfill = backfillProvider.GetRequiredService<ICustomerMemoryBackfillService>();
    var result = await backfill.RunAsync();
    Console.WriteLine(
        $"Backfill complete: businesses={result.BusinessesProcessed}, customers={result.CustomersProcessed}, facts={result.FactsWritten}");
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
services.AddScoped<IWhatsAppWebhookParserService, WhatsAppWebhookParserService>();
services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
services.AddScoped<IWorkingHoursService, WorkingHoursService>();
services.AddScoped<IAvailabilityService, AvailabilityService>();
services.AddScoped<ServiceNameResolver>();
services.AddScoped<ReservationPricingResolver>();
services.AddScoped<IPromotionPricingService, PromotionPricingService>();
services.AddScoped<IBusinessClock, BusinessClock>();
services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();
services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();
services.AddScoped<IAddOnCatalogService, AddOnCatalogService>();
services.AddScoped<IUsageBillingService, UsageBillingService>();
services.AddScoped<ICommerceService, CommerceService>();
services.AddScoped<ICommerceAdapter, LocalCommerceAdapter>();
services.AddHttpClient<SiigoCommerceAdapter>();
services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<SiigoCommerceAdapter>());
services.AddScoped<ICommerceAdapterFactory, CommerceAdapterFactory>();

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
services.AddScoped<ICustomerMemoryService, CustomerMemoryService>();
services.AddScoped<IRequestContextService, RequestContextService>();
services.AddScoped<ICustomerMemoryBackfillService, CustomerMemoryBackfillService>();
services.AddScoped<IReservationLifecycleService, ReservationLifecycleService>();
services.AddScoped<ICustomerReservationResolver, CustomerReservationResolver>();
services.AddScoped<IPaymentLifecycleService, PaymentLifecycleService>();
services.AddScoped<IReservationIntentBuilder, ReservationIntentBuilder>();
services.AddScoped<ICheckoutQuoteService, CheckoutQuoteService>();
services.AddScoped<ICheckoutPaymentCoordinator, CheckoutPaymentCoordinator>();
services.AddScoped<IEscalationNotifier, EscalationNotifier>();
services.AddScoped<IEscalationConfigProvider, EscalationConfigProvider>();
services.AddScoped<IReleaseLinkService, ReleaseLinkService>();
services.AddScoped<IConversationReleaseService, ConversationReleaseService>();
services.Configure<ReleaseLinkSettings>(configuration.GetSection(ReleaseLinkSettings.SectionName));

services.AddScoped<IWhatsAppService>(sp =>
    new ConsoleWhatsAppService(sp.GetRequiredService<ILogger<ConsoleWhatsAppService>>()));

services.AddScoped<IBlobStorageService>(sp =>
    new ConsoleBlobStorageService(sp.GetRequiredService<ILogger<ConsoleBlobStorageService>>()));

services.AddScoped<IIntegrationsConfigProvider, IntegrationsConfigProvider>();
services.AddScoped<ISchedulingPolicyProvider, SchedulingPolicyProvider>();
services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();
services.AddScoped<IPaymentConfirmationHandler, PaymentConfirmationHandler>();
services.AddScoped<IPaidCheckoutFulfillmentRegistry, PaidCheckoutFulfillmentRegistry>();
services.AddScoped<IPaidCheckoutFulfillmentHandler, ReservationPaidCheckoutFulfillmentHandler>();
services.AddScoped<IPaidCheckoutFulfillmentHandler, EnrollmentPaidCheckoutFulfillmentHandler>();
services.AddScoped<IPaidCheckoutFulfillmentHandler, OrderPaidCheckoutFulfillmentHandler>();
services.AddScoped<IMediaUrlResolver, ConsoleMediaUrlResolver>();
services.AddScoped<IOutboundMessageDispatcher, OutboundMessageDispatcher>();
services.AddScoped<IMessageSequenceResolver, MessageSequenceResolver>();
services.AddScoped<IActiveAgentConfigResolver, ActiveAgentConfigResolver>();
services.AddScoped<IEventNotificationDispatcher, EventNotificationDispatcher>();
services.AddScoped<IBusinessInboundContactRouter, BusinessInboundContactRouter>();
services.AddScoped<IExternalEscalationService, ExternalEscalationService>();
services.AddScoped<IExternalEscalationTargetHandler, OrderDeliveryExternalEscalationHandler>();

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
services.AddScoped<IAgentTool, PrepareOrderCheckoutTool>();
services.AddScoped<IAgentTool, CreateReservationTool>();
services.AddScoped<IAgentTool, AssignPaidSlotTool>();
services.AddScoped<IAgentTool, SuspendReservationTool>();
services.AddScoped<IAgentTool, GetCustomerReservationsTool>();
services.AddScoped<IAgentTool, ConfirmReservationAttendanceTool>();
services.AddScoped<IAgentTool, PrepareReservationChangeTool>();
services.AddScoped<IAgentTool, ConfirmReservationChangeTool>();
services.AddScoped<IAgentTool, VerifyPaymentTool>();
services.AddScoped<IAgentTool, EscalateToHumanTool>();
services.AddScoped<IAgentTool, GetServiceCatalogTool>();
services.AddScoped<IAgentTool, GetCompatibleAddOnsTool>();
services.AddScoped<IAgentTool, GetServiceFulfillmentTool>();
services.AddScoped<IAgentTool, SetFactTool>();
services.AddScoped<IAgentTool, ResetFlowContextTool>();
services.AddScoped<IAgentTool, SendMessageSequenceTool>();
services.AddScoped<IAgentTool, SearchProductsTool>();
services.AddScoped<IAgentTool, AddOrderItemTool>();
services.AddScoped<IAgentTool, RemoveOrderItemTool>();
services.AddScoped<IAgentTool, UpdateOrderItemQuantityTool>();
services.AddScoped<IAgentTool, GetOrderDraftTool>();
services.AddScoped<IAgentTool, CreateOrderTool>();
services.AddScoped<IAgentTool, StartExternalInteractionTool>();
services.AddScoped<IAgentTool, ResolveExternalInteractionTool>();
services.AddScoped<IAgentTool, CompleteExternalInteractionTool>();
services.AddScoped<IAgentTool, OperationsGetReservationsTool>();
services.AddScoped<IAgentTool, OperationsBlockAvailabilityTool>();
services.AddScoped<IAgentTool, OperationsRequestRescheduleTool>();
services.AddScoped<IAgentTool, OperationsBusinessMetricsTool>();
services.AddScoped<IAgentTool, OperationsCustomerHistoryTool>();

services.AddScoped<AgentToolRegistry>();
services.AddScoped<IAgentConversationService, AgentConversationService>();

// Build
var serviceProvider = services.BuildServiceProvider();

// Console UI
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

const string mimosBusinessIdStr = "FCEE3BA9-E6BF-43E2-8C1A-560CB724688B";
const string mimosBusinessName = "Vinos Artesanales Solorzano";
const string mimosAgentIdStr = "B0EE3BA9-E6BF-43E2-8C1A-560CB724688B";
const string mimosAgentName = "Camila";
const string mimosLegacyAgentName = "Camila";
const string mimosAgentDisplayName = "Camila";

var mimosBusinessId = Guid.Parse(mimosBusinessIdStr);
var mimosAgentId = Guid.Parse(mimosAgentIdStr);

Console.WriteLine("========================================================");
Console.WriteLine("  Vinos Artesanales Solorzano - Simulador Agentic Engine (FC)");
Console.WriteLine("========================================================");
Console.WriteLine();
Console.WriteLine($"  Negocio : {mimosBusinessName} ({mimosBusinessIdStr})");
Console.WriteLine($"  Agente  : {mimosAgentName}");
Console.WriteLine("  Escribe  'exit' para salir");
Console.WriteLine("  Escribe  'reset' para reiniciar la sesion");
Console.WriteLine();

// Simula el telefono del cliente (clave de sesion).
var userPhone = CreateTestUserPhone();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Tu: ");
    Console.ResetColor();

    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("salir", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Hasta luego!");
        break;
    }

    if (input.Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        userPhone = CreateTestUserPhone();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[sesion reiniciada - escribe tu siguiente mensaje]");
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

        var businessAgents = await agentRepo.GetByBusinessAsync(mimosBusinessId);
        var agentEntity = businessAgents.FirstOrDefault(a =>
            a.AgentId == mimosAgentId
            || a.Name.Equals(mimosAgentName, StringComparison.OrdinalIgnoreCase)
            || a.Name.Equals(mimosLegacyAgentName, StringComparison.OrdinalIgnoreCase));

        if (agentEntity == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: Agente '{mimosAgentName}' no encontrado para el negocio {mimosBusinessId}.");
            Console.ResetColor();
            Console.WriteLine();
            continue;
        }

        if (agentEntity.BusinessId != mimosBusinessId)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: El agente {agentEntity.AgentId} no pertenece al negocio configurado.");
            Console.ResetColor();
            Console.WriteLine();
            continue;
        }

        var conversation = await conversationService.GetOrCreateConversationAsync(
            agentEntity.BusinessId, userPhone, customerName: null);

        var result = await agentService.ProcessMessageAsync(
            agentEntity.AgentId,
            conversation.ConversationId,
            input,
            userPhone);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{mimosAgentDisplayName}: ");
        Console.ResetColor();

        if (!string.IsNullOrWhiteSpace(result.Response))
            Console.WriteLine(result.Response);

        foreach (var outbound in result.OutboundMessages)
        {
            if (!string.IsNullOrWhiteSpace(outbound.MediaUrl))
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine($"  [adjunto:{outbound.MediaType}] {outbound.Body ?? outbound.Filename ?? outbound.MediaUrl}");
                Console.ResetColor();
            }
            else if (!string.IsNullOrWhiteSpace(outbound.Body))
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine($"  {outbound.Body}");
                Console.ResetColor();
            }
        }

        if (string.IsNullOrWhiteSpace(result.Response)
            && result.OutboundMessages.Count == 0)
        {
            if (!result.Success)
                Console.WriteLine($"[error: {result.ErrorMessage}]");
            else
                Console.WriteLine("[sin respuesta]");
        }
        else if (!result.Success)
            Console.WriteLine($"[error: {result.ErrorMessage}]");

        Console.WriteLine();

        if (result.EscalatedToHuman)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Conversacion transferida a agente humano.");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {ex.Message}");
        if (ex.InnerException is not null)
            Console.WriteLine($"  Causa: {ex.InnerException.Message}");
        Console.ResetColor();
        Console.WriteLine();
    }
}

static string CreateTestUserPhone()
{
    return $"+1555{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 10000000:0000000}";
}
