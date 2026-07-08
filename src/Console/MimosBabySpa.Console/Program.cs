using System.Text.Json;

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
using MimosBabySpa.Application.Agents.Runtime;

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



var environmentConfiguration = new Dictionary<string, string?>();

AddEnvironmentOverride(environmentConfiguration, "ConnectionStrings:DefaultConnection", "ConnectionStrings__DefaultConnection");

AddEnvironmentOverride(environmentConfiguration, "OpenAI:TextModel:ApiKey", "OpenAI__TextModel__ApiKey");

AddEnvironmentOverride(environmentConfiguration, "OpenAI:TextModel:Endpoint", "OpenAI__TextModel__Endpoint");

AddEnvironmentOverride(environmentConfiguration, "OpenAI:TextModel:DeploymentName", "OpenAI__TextModel__DeploymentName");

AddEnvironmentOverride(environmentConfiguration, "OpenAI:AudioModel:ApiKey", "OpenAI__AudioModel__ApiKey");

AddEnvironmentOverride(environmentConfiguration, "OpenAI:AudioModel:Endpoint", "OpenAI__AudioModel__Endpoint");

AddEnvironmentOverride(environmentConfiguration, "OpenAI:AudioModel:DeploymentName", "OpenAI__AudioModel__DeploymentName");



var configuration = new ConfigurationBuilder()

    .SetBasePath(AppContext.BaseDirectory)

    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)

    .AddJsonFile("appSettings.json", optional: true, reloadOnChange: true)

    .AddInMemoryCollection(environmentConfiguration)

    .Build();



static void AddEnvironmentOverride(IDictionary<string, string?> values, string key, string environmentName)

{

    var value = Environment.GetEnvironmentVariable(environmentName);

    if (!string.IsNullOrWhiteSpace(value))

        values[key] = value;

}



if (args is ["backfill-customer-memory"])

{

    var backfillServices = new ServiceCollection();

    backfillServices.AddSingleton<IConfiguration>(configuration);

    backfillServices.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

    backfillServices.AddDbContext<ApplicationDbContext>(options =>

        options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));



    backfillServices.AddMemoryCache();

    backfillServices.AddSingleton<AgentToolMetadataRegistry>();

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

services.AddScoped<ServiceSelectionResolver>();

services.AddScoped<ReservationPricingResolver>();

services.AddScoped<IPromotionPricingService, PromotionPricingService>();

services.AddScoped<IBusinessClock, BusinessClock>();

services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();

services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();

services.AddScoped<IAddOnCatalogService, AddOnCatalogService>();

services.AddScoped<IUsageBillingService, ConsoleUsageBillingService>();

services.AddScoped<IProductCatalogAvailabilityService, ProductCatalogAvailabilityService>();

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

services.AddSingleton<AgentToolMetadataRegistry>();

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

        services.AddScoped<IExternalEscalationOutcomePublisher, ExternalEscalationOutcomePublisher>();



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

services.AddScoped<IAgentTool, OperationsGetReservationsTool>();

services.AddScoped<IAgentTool, OperationsBlockAvailabilityTool>();

services.AddScoped<IAgentTool, OperationsRequestRescheduleTool>();

services.AddScoped<IAgentTool, OperationsBusinessMetricsTool>();

services.AddScoped<IAgentTool, OperationsCustomerHistoryTool>();



services.AddScoped<AgentToolRegistry>();

services.AddScoped<IAgentConversationService, AgentConversationService>();



// Build

var serviceProvider = services.BuildServiceProvider();

if (args.Length >= 2 && args[0].Equals("confirm-payment", StringComparison.OrdinalIgnoreCase))
{
    var paymentReferenceId = args[1];
    await using var scope = serviceProvider.CreateAsyncScope();
    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
    var confirmationHandler = scope.ServiceProvider.GetRequiredService<IPaymentConfirmationHandler>();
    var payment = await unitOfWork.PaymentTransactions.GetByPaymentReferenceIdAsync(paymentReferenceId);
    if (payment is null)
    {
        Console.WriteLine($"Payment reference not found: {paymentReferenceId}");
        return;
    }

    var payload = System.Text.Json.JsonSerializer.Serialize(new
    {
        source = "console_confirm_payment",
        confirmed_at = DateTime.UtcNow
    });
    var result = await confirmationHandler.HandleAsync(
        payment.PaymentReferenceId,
        $"manual:{Guid.NewGuid():N}",
        payment.AmountInCents,
        payload,
        sourceOverride: MimosBabySpa.Domain.Enums.PaymentTransactionSource.Manual);

    Console.WriteLine(result.Success
        ? $"Payment confirmed: {payment.PaymentReferenceId}"
        : $"Payment confirmation failed: {result.ErrorMessage}");
    return;
}



// Console UI

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.InputEncoding = System.Text.Encoding.UTF8;



var consoleAgent = ConsoleAgentOptions.FromEnvironment();

var traceEnabled = IsTraceEnabled(args);

var scriptedMessages = BuildLuisReservationScript(args);

if (scriptedMessages.Count > 0)

    traceEnabled = true;

Console.WriteLine("========================================================");
Console.WriteLine($"  {consoleAgent.BusinessName} - Simulador Agentic Engine (FC)");
Console.WriteLine("========================================================");
Console.WriteLine();
Console.WriteLine($"  Negocio : {consoleAgent.BusinessName} ({consoleAgent.BusinessId})");
Console.WriteLine($"  Agente  : {consoleAgent.AgentName}");

Console.WriteLine("  Escribe  'exit' para salir");

Console.WriteLine("  Escribe  'reset' para reiniciar la sesion");

Console.WriteLine("  Usa     --trace para ver system prompt, tools y respuestas del LLM");

Console.WriteLine("  Usa     test-luis-reserva para correr una prueba automatica con traza");

Console.WriteLine();



// Simula el telefono del cliente (clave de sesion).

var userPhone = CreateTestUserPhone();

var customerName = Environment.GetEnvironmentVariable("TALKIO_CONSOLE_CUSTOMER_NAME");



while (true)

{

    Console.ForegroundColor = ConsoleColor.Cyan;

    Console.Write("Tu: ");

    Console.ResetColor();



    string? input;

    if (scriptedMessages.Count > 0)

    {

        input = scriptedMessages.Dequeue();

        Console.WriteLine(input);

    }

    else

    {

        input = Console.ReadLine();

    }



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



        var businessAgents = await agentRepo.GetByBusinessAsync(consoleAgent.BusinessId);

        var agentEntity = businessAgents.FirstOrDefault(a =>

            a.AgentId == consoleAgent.AgentId

            || a.Name.Equals(consoleAgent.AgentName, StringComparison.OrdinalIgnoreCase)

            || consoleAgent.AgentAliases.Any(alias => a.Name.Equals(alias, StringComparison.OrdinalIgnoreCase)));



        if (agentEntity == null)

        {

            Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine($"ERROR: Agente '{consoleAgent.AgentName}' no encontrado para el negocio {consoleAgent.BusinessId}.");

            Console.ResetColor();

            Console.WriteLine();

            continue;

        }



        if (agentEntity.BusinessId != consoleAgent.BusinessId)

        {

            Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine($"ERROR: El agente {agentEntity.AgentId} no pertenece al negocio configurado.");

            Console.ResetColor();

            Console.WriteLine();

            continue;

        }



        var conversation = await conversationService.GetOrCreateConversationAsync(

            agentEntity.BusinessId, userPhone, customerName: customerName);



        var result = await agentService.ProcessMessageAsync(

            agentEntity.AgentId,

            conversation.ConversationId,

            input,

            userPhone);



        Console.ForegroundColor = ConsoleColor.Green;

        Console.Write($"{consoleAgent.AgentDisplayName}: ");

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



        if (traceEnabled)

            PrintTurnTrace(result.Trace);



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



        if (scriptedMessages.Count == 0 && IsScriptedReservationTest(args))

            break;



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




static bool IsTraceEnabled(string[] args) =>
    args.Any(a => a.Equals("--trace", StringComparison.OrdinalIgnoreCase)
               || a.Equals("trace", StringComparison.OrdinalIgnoreCase))
    || string.Equals(Environment.GetEnvironmentVariable("TALKIO_CONSOLE_TRACE"), "true", StringComparison.OrdinalIgnoreCase);

static bool IsScriptedReservationTest(string[] args) =>
    args.Any(a => a.Equals("test-luis-reserva", StringComparison.OrdinalIgnoreCase)
               || a.Equals("--test-luis-reserva", StringComparison.OrdinalIgnoreCase)
               || a.Equals("luis-reserva-trace", StringComparison.OrdinalIgnoreCase));

static Queue<string> BuildLuisReservationScript(string[] args)
{
    if (!IsScriptedReservationTest(args))
        return new Queue<string>();

    var custom = Environment.GetEnvironmentVariable("TALKIO_CONSOLE_TEST_MESSAGES");
    var messages = string.IsNullOrWhiteSpace(custom)
        ? new[]
        {
            "Hola, quiero reservar un corte basico de adulto manana a las 10:30 de la manana",
            "Sin adicionales",
            "Mi nombre es Carlos Perez y naci el 1990-01-01",
        }
        : custom.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    return new Queue<string>(messages);
}

static void PrintTurnTrace(IReadOnlyList<AgentTurnTraceEntry> trace)
{
    if (trace.Count == 0)
        return;

    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine();
    Console.WriteLine("--- TRACE LLM ---");
    Console.ResetColor();

    foreach (var entry in trace)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[{entry.Kind}] iter={entry.Iteration} stage={entry.StageId ?? "(sin etapa)"}");
        Console.ResetColor();

        if (entry.EnabledTools.Count > 0)
            Console.WriteLine($"tools: {string.Join(", ", entry.EnabledTools)}");

        if (!string.IsNullOrWhiteSpace(entry.FinishReason))
            Console.WriteLine($"finish: {entry.FinishReason}");

        if (entry.ToolCalls.Count > 0)
        {
            Console.WriteLine("tool_calls:");
            foreach (var toolCall in entry.ToolCalls)
            {
                Console.WriteLine($"- {toolCall.FunctionName}({toolCall.ArgumentsJson}) id={toolCall.Id}");
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.ToolName))
        {
            Console.WriteLine($"tool: {entry.ToolName}({entry.ToolArgumentsJson})");
            Console.WriteLine("tool_result_visible_para_llm:");
            Console.WriteLine(PrettyJson(entry.ToolResultJson, jsonOptions));
        }

        if (!string.IsNullOrWhiteSpace(entry.Content))
        {
            Console.WriteLine(entry.Kind == "system_prompt" ? "system_prompt:" : "respuesta_llm:");
            Console.WriteLine(entry.Content);
        }

        Console.WriteLine();
    }
}

static string PrettyJson(string? json, JsonSerializerOptions options)
{
    if (string.IsNullOrWhiteSpace(json))
        return string.Empty;

    try
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, options);
    }
    catch (JsonException)
    {
        return json;
    }
}

static string CreateTestUserPhone()

{

    var configuredPhone = Environment.GetEnvironmentVariable("TALKIO_CONSOLE_PHONE");

    if (!string.IsNullOrWhiteSpace(configuredPhone))

        return configuredPhone.Trim();



    return $"+1555{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 10000000:0000000}";

}

internal sealed record ConsoleAgentOptions(
    Guid BusinessId,
    string BusinessName,
    Guid AgentId,
    string AgentName,
    string AgentDisplayName,
    IReadOnlyList<string> AgentAliases)
{
    public static ConsoleAgentOptions FromEnvironment()
    {
        var businessId = GetGuid(
            "TALKIO_CONSOLE_BUSINESS_ID",
            "BABA0000-0000-0000-0000-000000000001");
        var agentId = GetGuid(
            "TALKIO_CONSOLE_AGENT_ID",
            "BABA0000-0000-0000-0000-000000000002");
        var agentName = GetText("TALKIO_CONSOLE_AGENT_NAME", "Luis");

        return new ConsoleAgentOptions(
            businessId,
            GetText("TALKIO_CONSOLE_BUSINESS_NAME", "Luis Petit Profesional Barber"),
            agentId,
            agentName,
            GetText("TALKIO_CONSOLE_AGENT_DISPLAY_NAME", agentName),
            GetList("TALKIO_CONSOLE_AGENT_ALIASES", "Luis Petit"));
    }

    private static Guid GetGuid(string name, string fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return Guid.Parse(string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim());
    }

    private static string GetText(string name, string fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
    }

    private static IReadOnlyList<string> GetList(string name, string fallback)
    {
        var raw = GetText(name, fallback);
        return raw
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}
internal sealed class ConsoleUsageBillingService : IUsageBillingService

{

    public Task<UsageGateResult> CanProcessAsync(Guid businessId, CancellationToken ct = default) =>

        Task.FromResult(new UsageGateResult(true, "console_test_mode", "Allowed in console simulator.", null));



    public Task<UsageChargeResult> ChargeAsync(UsageChargeRequest request, CancellationToken ct = default) =>

        Task.FromResult(new UsageChargeResult(true, 0, 0, null));



    public Task<BusinessUsageSnapshot?> GetCurrentUsageAsync(Guid businessId, CancellationToken ct = default) =>

        Task.FromResult<BusinessUsageSnapshot?>(null);



    public Task<IReadOnlyList<UsagePlanDto>> GetPlansAsync(CancellationToken ct = default) =>

        Task.FromResult<IReadOnlyList<UsagePlanDto>>(Array.Empty<UsagePlanDto>());

}

