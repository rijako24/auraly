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



// Core Repositories

services.AddScoped<IUnitOfWork, UnitOfWork>();

services.AddScoped<IConversationRepository, ConversationRepository>();

services.AddScoped<IConversationStateRepository, ConversationStateRepository>();

services.AddScoped<IMessageRepository, MessageRepository>();

services.AddScoped<ILeadRepository, LeadRepository>();

services.AddScoped<IReservationRepository, ReservationRepository>();

services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();

services.AddScoped<MimosBabySpa.Domain.Repositories.IAgentRepository, AgentRepository>();



// Application Services

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
services.AddScoped<IServiceCatalogPricingService, ServiceCatalogPricingService>();

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



// OpenAI Clients

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



// Supporting Infrastructure



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



// Business Rules Engine

services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();



// Agentic Engine (Function Calling)

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
services.AddScoped<IFlowRouter, FlowRouter>();
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

services.AddScoped<IAgentTool, GetCustomerReservationsTool>();

services.AddScoped<IAgentTool, ManageReservationTool>();

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

var scriptedScenario = ResolveScriptedScenario(args);
consoleAgent = ResolveScenarioAgent(scriptedScenario) ?? consoleAgent;
var scriptedMessages = BuildLuisReservationScript(args);
var scriptedCaseIndex = 0;
var scriptedCheckoutState = new ConsoleCheckoutState();

if (scriptedMessages.Count > 0 && !string.Equals(scriptedScenario, "luis-critical-flow", StringComparison.OrdinalIgnoreCase))

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
Console.WriteLine("  Usa     test-luis-catalogo para validar que hola consulte catalogo oficial");
Console.WriteLine("  Usa     test-luis-critical-flow para correr escenarios criticos contra BD/config real");
Console.WriteLine("  Usa     test-mimos-critical-flow / test-auraly-critical-flow / test-rada-critical-flow / test-solorzano-critical-flow / test-cjdistribuciones-critical-flow");

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

        userPhone = CreateFreshTestUserPhone();

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



        if (IsConfirmActivePaymentCommand(input))
        {
            using var scriptedPaymentTimeout = CreateScriptedTurnTimeout(scriptedScenario);
            var confirmed = await ConfirmLatestConversationPaymentAsync(
                scope.ServiceProvider,
                conversation.ConversationId,
                scriptedScenario,
                scriptedCaseIndex,
                scriptedPaymentTimeout?.Token ?? CancellationToken.None);

            if (!confirmed)
            {
                Environment.ExitCode = 1;
                break;
            }

            Console.WriteLine();
            if (scriptedScenario is not null)
                scriptedCaseIndex++;

            if (scriptedMessages.Count == 0 && IsScriptedConsoleTest(args))
                break;

            continue;
        }



        using var scriptedTurnTimeout = CreateScriptedTurnTimeout(scriptedScenario);

        var processTurnTask = agentService.ProcessMessageAsync(

            agentEntity.AgentId,

            conversation.ConversationId,

            input,

            userPhone,

            cancellationToken: scriptedTurnTimeout?.Token ?? CancellationToken.None);

        var result = scriptedTurnTimeout is null

            ? await processTurnTask

            : await processTurnTask.WaitAsync(scriptedTurnTimeout.Token);



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
        {
            PrintTurnTrace(result.Trace);
        }

        if (scriptedScenario is not null && !result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {scriptedScenario} #{scriptedCaseIndex + 1:00}: el turno fallo: {result.ErrorMessage}");
            Console.ResetColor();
            Environment.ExitCode = 1;
            break;
        }

        if (!ValidateScriptedTurn(scriptedScenario, scriptedCaseIndex, input, result.Response, result.Trace, scriptedCheckoutState))
        {
            Environment.ExitCode = 1;
            break;
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

        if (scriptedScenario is not null)
            scriptedCaseIndex++;

        if (scriptedMessages.Count == 0 && IsScriptedConsoleTest(args))

            break;



        if (result.EscalatedToHuman)

        {

            Console.ForegroundColor = ConsoleColor.Yellow;

            Console.WriteLine("  Conversacion transferida a agente humano.");

            Console.ResetColor();

            Console.WriteLine();

        }

    }

    catch (OperationCanceledException) when (scriptedScenario is not null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] {scriptedScenario}: el turno excedio el timeout configurado.");
        Console.ResetColor();
        Environment.ExitCode = 1;
        break;
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




static CancellationTokenSource? CreateScriptedTurnTimeout(string? scenario)
{
    if (scenario is null)
        return null;

    var timeoutSeconds = 90;
    var configured = Environment.GetEnvironmentVariable("TALKIO_CONSOLE_TEST_TIMEOUT_SECONDS");
    if (int.TryParse(configured, out var parsed) && parsed > 0)
        timeoutSeconds = parsed;

    return new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
}
static bool IsConfirmActivePaymentCommand(string input) =>
    input.Equals("__confirm_active_payment", StringComparison.OrdinalIgnoreCase);

static async Task<bool> ConfirmLatestConversationPaymentAsync(
    IServiceProvider services,
    Guid conversationId,
    string? scenario,
    int caseIndex,
    CancellationToken cancellationToken)
{
    var paymentLifecycle = services.GetRequiredService<IPaymentLifecycleService>();
    var confirmationHandler = services.GetRequiredService<IPaymentConfirmationHandler>();
    var payment = await paymentLifecycle.GetLatestByConversationAsync(conversationId, cancellationToken);

    if (payment is null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] {scenario ?? "console"} #{caseIndex + 1:00}: no hay checkout activo para confirmar.");
        Console.ResetColor();
        return false;
    }

    var payload = JsonSerializer.Serialize(new
    {
        source = "console-critical-flow",
        payment.PaymentReferenceId,
        payment.AmountInCents,
        status = "APPROVED"
    });

    var result = await confirmationHandler.HandleAsync(
        payment.PaymentReferenceId,
        $"console:{Guid.NewGuid():N}",
        payment.AmountInCents,
        payload,
        cancellationToken,
        MimosBabySpa.Domain.Enums.PaymentTransactionSource.Manual);

    if (!result.Success)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] {scenario ?? "console"} #{caseIndex + 1:00}: confirmacion de pago fallo: {result.ErrorMessage}");
        Console.ResetColor();
        return false;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[PASS] {scenario ?? "test-luis-critical-flow"} #{caseIndex + 1:00}: pago confirmado y fulfillment ejecutado para {payment.PaymentReferenceId}.");
    Console.ResetColor();
    return true;
}
static bool IsTraceEnabled(string[] args) =>
    args.Any(a => a.Equals("--trace", StringComparison.OrdinalIgnoreCase)
               || a.Equals("trace", StringComparison.OrdinalIgnoreCase))
    || string.Equals(Environment.GetEnvironmentVariable("TALKIO_CONSOLE_TRACE"), "true", StringComparison.OrdinalIgnoreCase);

static string? ResolveScriptedScenario(string[] args)
{
    var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["test-luis-critical-flow"] = "luis-critical-flow",
        ["--test-luis-critical-flow"] = "luis-critical-flow",
        ["test-mimos-critical-flow"] = "mimos-critical-flow",
        ["--test-mimos-critical-flow"] = "mimos-critical-flow",
        ["test-auraly-critical-flow"] = "auraly-critical-flow",
        ["--test-auraly-critical-flow"] = "auraly-critical-flow",
        ["test-rada-critical-flow"] = "rada-critical-flow",
        ["--test-rada-critical-flow"] = "rada-critical-flow",
        ["test-solorzano-critical-flow"] = "solorzano-critical-flow",
        ["--test-solorzano-critical-flow"] = "solorzano-critical-flow",
        ["test-cjdistribuciones-critical-flow"] = "cjdistribuciones-critical-flow",
        ["--test-cjdistribuciones-critical-flow"] = "cjdistribuciones-critical-flow",
        ["test-luis-catalogo"] = "luis-catalogo",
        ["--test-luis-catalogo"] = "luis-catalogo"
    };

    foreach (var arg in args)
    {
        if (known.TryGetValue(arg, out var scenario))
            return scenario;
    }

    if (IsScriptedReservationTest(args))
        return "luis-reserva";

    return null;
}

static bool IsScriptedConsoleTest(string[] args) => ResolveScriptedScenario(args) is not null;

static bool IsScriptedReservationTest(string[] args) =>
    args.Any(a => a.Equals("test-luis-reserva", StringComparison.OrdinalIgnoreCase)
               || a.Equals("--test-luis-reserva", StringComparison.OrdinalIgnoreCase)
               || a.Equals("luis-reserva-trace", StringComparison.OrdinalIgnoreCase));

static Queue<string> BuildLuisReservationScript(string[] args)
{
    var scenario = ResolveScriptedScenario(args);
    if (scenario is null)
        return new Queue<string>();

    var custom = Environment.GetEnvironmentVariable("TALKIO_CONSOLE_TEST_MESSAGES");
    if (!string.IsNullOrWhiteSpace(custom))
        return new Queue<string>(custom.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    var messages = scenario switch
    {
        "luis-catalogo" => new[] { "hola" },
        "luis-critical-flow" => BuildLuisCriticalFlowMessages(),
        "mimos-critical-flow" or "auraly-critical-flow" or "rada-critical-flow" or "solorzano-critical-flow" or "cjdistribuciones-critical-flow" => BuildConsoleScenarioMessages(GetConsoleScenarioSteps(scenario)),
        _ => new[]
        {
            "Hola, quiero reservar un corte basico de adulto manana a las 10:30 de la manana",
            "Sin adicionales",
            "Mi nombre es Carlos Perez y naci el 1990-01-01",
        }
    };

    return new Queue<string>(messages);
}

static bool ValidateScriptedTurn(
    string? scenario,
    int caseIndex,
    string input,
    string? response,
    IReadOnlyList<AgentTurnTraceEntry> trace,
    ConsoleCheckoutState checkoutState)
{
    if (string.Equals(scenario, "luis-catalogo", StringComparison.OrdinalIgnoreCase))
    {
        if (TraceContainsTool(trace, "get_service_catalog") && TraceToolResultContains(trace, "get_service_catalog", "## CATEGORIAS DE SERVICIOS"))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[PASS] test-luis-catalogo: get_service_catalog fue invocado y devolvio categorias.");
            Console.ResetColor();
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[FAIL] test-luis-catalogo: el agente no invoco get_service_catalog o no devolvio categorias.");
        Console.ResetColor();
        return false;
    }

    var steps = GetConsoleScenarioSteps(scenario);
    if (steps.Count == 0)
        return true;
    if (caseIndex < 0 || caseIndex >= steps.Count)
        return true;

    var current = steps[caseIndex];
    var missingTools = current.ExpectedTools
        .Where(tool => !TraceContainsTool(trace, tool))
        .ToArray();
    var forbiddenTools = current.ForbiddenTools
        .Where(tool => TraceContainsTool(trace, tool))
        .ToArray();
    var missingToolResults = current.ToolResultContains
        .Where(expectation => !TraceToolResultContains(trace, expectation.ToolName, expectation.Text))
        .ToArray();
    var missingStages = current.ExpectedStages
        .Where(stage => !TraceContainsStage(trace, stage))
        .ToArray();
    var forbiddenStages = current.ForbiddenStages
        .Where(stage => TraceContainsStage(trace, stage))
        .ToArray();
    var missingResponseAny = current.ResponseContainsAny.Count > 0
        && !current.ResponseContainsAny.Any(expected => ResponseContains(response, expected));
    var missingResponseAll = current.ResponseContainsAll
        .Where(expected => !ResponseContains(response, expected))
        .ToArray();
    var forbiddenResponse = current.ResponseForbiddenContains
        .Where(forbidden => ResponseContains(response, forbidden))
        .ToArray();
    var toolOrderFailure = ValidateToolOrder(current, trace);
    var checkoutFailure = ValidateCheckoutExpectation(current, trace, checkoutState);

    if (missingTools.Length == 0
        && forbiddenTools.Length == 0
        && missingToolResults.Length == 0
        && missingStages.Length == 0
        && forbiddenStages.Length == 0
        && !missingResponseAny
        && missingResponseAll.Length == 0
        && forbiddenResponse.Length == 0
        && toolOrderFailure is null
        && checkoutFailure is null)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        var expected = current.ExpectedTools.Count == 0 ? "sin tool obligatoria" : string.Join(", ", current.ExpectedTools);
        var forbidden = current.ForbiddenTools.Count == 0 ? string.Empty : $"; prohibidas ausentes: {string.Join(", ", current.ForbiddenTools)}";
        var expectedResults = current.ToolResultContains.Count == 0 ? string.Empty : $"; resultados validados: {string.Join(", ", current.ToolResultContains.Select(e => e.ToolName))}";
        var expectedStages = current.ExpectedStages.Count == 0 ? string.Empty : $"; etapas: {string.Join(", ", current.ExpectedStages)}";
        Console.WriteLine($"[PASS] {scenario} #{caseIndex + 1:00} {current.Id}: {expected}{forbidden}{expectedResults}{expectedStages}");
        if (caseIndex == steps.Count - 1)
            Console.WriteLine($"[PASS] {scenario}: {steps.Count}/{steps.Count} pasos criticos pasaron.");
        Console.ResetColor();
        return true;
    }

    Console.ForegroundColor = ConsoleColor.Red;
    var failures = new List<string>();
    if (missingTools.Length > 0)
        failures.Add($"faltan tools {string.Join(", ", missingTools)}");
    if (forbiddenTools.Length > 0)
        failures.Add($"tools prohibidas invocadas {string.Join(", ", forbiddenTools)}");
    if (missingToolResults.Length > 0)
        failures.Add($"resultados esperados ausentes {string.Join(", ", missingToolResults.Select(e => $"{e.ToolName}:{e.Text}"))}");
    if (missingStages.Length > 0)
        failures.Add($"faltan etapas {string.Join(", ", missingStages)}");
    if (forbiddenStages.Length > 0)
        failures.Add($"etapas prohibidas presentes {string.Join(", ", forbiddenStages)}");
    if (missingResponseAny)
        failures.Add($"respuesta no contiene ninguno de [{string.Join(", ", current.ResponseContainsAny)}]");
    if (missingResponseAll.Length > 0)
        failures.Add($"respuesta no contiene [{string.Join(", ", missingResponseAll)}]");
    if (forbiddenResponse.Length > 0)
        failures.Add($"respuesta contiene texto prohibido [{string.Join(", ", forbiddenResponse)}]");
    if (toolOrderFailure is not null)
        failures.Add(toolOrderFailure);
    if (checkoutFailure is not null)
        failures.Add(checkoutFailure);
    Console.WriteLine($"[FAIL] {scenario} #{caseIndex + 1:00} {current.Id}: {string.Join("; ", failures)} para '{input}'. Respuesta: {response}");
    Console.ResetColor();
    return false;
}

static string? ValidateToolOrder(ConsoleScenarioStep current, IReadOnlyList<AgentTurnTraceEntry> trace)
{
    if (current.ExpectedToolOrder.Count == 0)
        return null;

    var sequence = trace
        .Where(entry => !string.IsNullOrWhiteSpace(entry.ToolName) && !IsSkippedStaleToolTrace(entry))
        .Select(entry => entry.ToolName!)
        .ToArray();

    var searchFrom = 0;
    foreach (var expected in current.ExpectedToolOrder)
    {
        var foundAt = Array.FindIndex(
            sequence,
            searchFrom,
            toolName => string.Equals(toolName, expected, StringComparison.OrdinalIgnoreCase));
        if (foundAt < 0)
            return $"orden de tools invalido; esperado {string.Join(" -> ", current.ExpectedToolOrder)}; ejecutado {string.Join(" -> ", sequence)}";

        searchFrom = foundAt + 1;
    }

    return null;
}
static string? ValidateCheckoutExpectation(
    ConsoleScenarioStep current,
    IReadOnlyList<AgentTurnTraceEntry> trace,
    ConsoleCheckoutState checkoutState)
{
    var checkout = ExtractLastCheckout(trace);
    if (checkout is null)
        return current.CheckoutExpectation is CheckoutExpectation.RequiresSameAsPrevious or CheckoutExpectation.RequiresDifferentFromPrevious
            ? "no hubo checkout para comparar link vigente"
            : null;

    if (current.CheckoutExpectation == CheckoutExpectation.RequiresSameAsPrevious)
    {
        if (string.IsNullOrWhiteSpace(checkoutState.LastPaymentTransactionId))
            return "no habia checkout previo para validar reutilizacion de link";

        if (!checkout.PaymentTransactionId.Equals(checkoutState.LastPaymentTransactionId, StringComparison.OrdinalIgnoreCase))
            return $"checkout genero pago nuevo {checkout.PaymentTransactionId}; esperado reutilizar {checkoutState.LastPaymentTransactionId}";
    }

    if (current.CheckoutExpectation == CheckoutExpectation.RequiresDifferentFromPrevious)
    {
        if (string.IsNullOrWhiteSpace(checkoutState.LastPaymentTransactionId))
            return "no habia checkout previo para validar link nuevo";

        if (checkout.PaymentTransactionId.Equals(checkoutState.LastPaymentTransactionId, StringComparison.OrdinalIgnoreCase))
            return $"checkout reutilizo {checkout.PaymentTransactionId}; esperado generar un pago nuevo";
    }

    checkoutState.LastPaymentTransactionId = checkout.PaymentTransactionId;
    checkoutState.LastPaymentUrl = checkout.PaymentUrl;
    return null;
}

static string[] BuildLuisCriticalFlowMessages()
{
    var messages = new List<string>();
    var first = true;
    foreach (var step in GetLuisCriticalFlowSteps())
    {
        if (!first && step.ResetBefore)
            messages.Add("reset");

        messages.Add(step.UserMessage);
        first = false;
    }

    return messages.ToArray();
}
static string[] BuildConsoleScenarioMessages(IReadOnlyList<ConsoleScenarioStep> steps)
{
    var messages = new List<string>();
    var first = true;
    foreach (var step in steps)
    {
        if (!first && step.ResetBefore)
            messages.Add("reset");

        messages.Add(step.UserMessage);
        first = false;
    }

    return messages.ToArray();
}

static IReadOnlyList<ConsoleScenarioStep> GetConsoleScenarioSteps(string? scenario) => scenario?.ToLowerInvariant() switch
{
    "luis-critical-flow" => GetLuisCriticalFlowSteps(),
    "mimos-critical-flow" => GetMimosCriticalFlowSteps(),
    "auraly-critical-flow" => GetAuralyCriticalFlowSteps(),
    "rada-critical-flow" => GetRadaCriticalFlowSteps(),
    "solorzano-critical-flow" => GetSolorzanoCriticalFlowSteps(),
    "cjdistribuciones-critical-flow" => GetCjDistribucionesCriticalFlowSteps(),
    _ => []
};


static IReadOnlyList<ConsoleScenarioStep> GetLuisCriticalFlowSteps()
{
    var uniqueOffsetDays = 3650 + (int)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Environment.ProcessId + Random.Shared.Next(0, 10000)) % 2000);
    var dateCursor = DateTime.Today.AddDays(uniqueOffsetDays);
    var scenarioDates = new List<string>();
    while (scenarioDates.Count < 4)
    {
        if (dateCursor.DayOfWeek is not DayOfWeek.Sunday)
            scenarioDates.Add(dateCursor.ToString("yyyy-MM-dd"));

        dateCursor = dateCursor.AddDays(1);
    }

    var happyDate = scenarioDates[0];
    var secondDate = scenarioDates[1];

    return
    [
        new("booking1_search_services", "mano para agendar para un corte de cabello", ["get_service_catalog"])
        {
            ExpectedStages = ["discovery"],
            ForbiddenTools = ["get_compatible_add_ons", "check_availability", "prepare_checkout", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("get_service_catalog", "## CATALOGO DE SERVICIOS"), new("get_service_catalog", "Corte")],
            ResponseContainsAll = ["bienvenido", "BARBER KIDS MENS", "Soy Luis Petit", "barbero profesional", "servicio", "interesado"],
            ResponseForbiddenContains = ["tu barbero profesional", "categorias de servicios", "adicionales", "anticipo", "resumen", "hoy"]
        },
        new("booking1_service", "Quiero corte basico de adulto", ["resolve_service_selection", "get_compatible_add_ons"])
        {
            ResetBefore = false,
            ExpectedStages = ["discovery", "add_ons"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("resolve_service_selection", "Corte basico de adulto")],
            ResponseContainsAll = ["Quieres agregar alguno de estos adicionales o seguimos sin ellos"],
            ResponseForbiddenContains = ["anticipo", "resumen"]
        },
        new("booking1_no_addons", "Sin adicionales", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["add_ons"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "check_availability", "prepare_checkout", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("set_fact", "add_ons"), new("set_fact", "ninguno")],
            ResponseContainsAny = ["dia", "fecha", "cuando", "hora"],
            ResponseForbiddenContains = ["Quieres agregar", "agregar alguno", "resumen", "link"]
        },
        new("booking1_date_slots", $"Que espacios tienes para el {happyDate}", ["set_fact", "check_availability"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ExpectedToolOrder = ["set_fact", "check_availability"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "prepare_checkout", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("set_fact", "desired_date")],
            ResponseContainsAny = ["8:30", "hora", "horario", "espacio"],
            ResponseForbiddenContains = ["Quieres agregar", "agregar alguno", "resumen", "link"]
        },
        new("booking1_pick_time", "A las 8:30", ["set_fact", "check_availability"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ExpectedToolOrder = ["set_fact", "check_availability"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "prepare_checkout", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("set_fact", "desired_time"), new("check_availability", "availability_checked")],
            ResponseContainsAny = ["nombre", "datos", "naci", "nacimiento"],
            ResponseForbiddenContains = ["Quieres agregar", "agregar alguno", "resumen", "link"]
        },
        new("booking1_checkout", "Soy Luis Critical, naci el 1990-01-01 y mi correo es luis.critical@example.com", ["set_fact", "prepare_checkout"])
        {
            ResetBefore = false,
            ExpectedStages = ["customer_data", "finalization"],
            ExpectedToolOrder = ["set_fact", "prepare_checkout"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("set_fact", "customer_name"), new("prepare_checkout", "checkout_token")],
            ResponseContainsAny = ["anticipo", "pago", "resumen", "link"],
            ResponseForbiddenContains = ["Genero el resumen", "Quieres que genere", "generar el resumen con estos datos"]
        },
        new("booking1_change_time_same_link", "Cambia la hora a las 9:00 y actualiza el resumen", ["set_fact", "check_availability", "prepare_checkout"])
        {
            ResetBefore = false,
            ExpectedStages = ["finalization"],
            ExpectedToolOrder = ["set_fact", "check_availability", "prepare_checkout"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("set_fact", "desired_time"), new("check_availability", "availability_checked"), new("prepare_checkout", "checkout_token")],
            CheckoutExpectation = CheckoutExpectation.RequiresSameAsPrevious,
            ResponseContainsAny = ["anticipo", "pago", "resumen", "link"]
        },
        new("booking1_change_service_requires_addons", "Cambia el servicio a corte + barba con terminacion premium", ["resolve_service_selection", "get_compatible_add_ons"])
        {
            ResetBefore = false,
            ExpectedStages = ["finalization", "add_ons"],
            ForbiddenTools = ["check_availability", "prepare_checkout", "manage_reservation", "get_customer_reservations"],
            ResponseContainsAll = ["Quieres agregar alguno de estos adicionales o seguimos sin ellos"],
            ResponseForbiddenContains = ["resumen", "link"]
        },
        new("booking1_change_service_new_link", "Sin adicionales, actualiza el resumen y link", ["set_fact", "check_availability", "prepare_checkout"])
        {
            ResetBefore = false,
            ExpectedStages = ["add_ons", "scheduling", "finalization"],
            ExpectedToolOrder = ["set_fact", "check_availability", "prepare_checkout"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("set_fact", "add_ons"), new("check_availability", "availability_checked"), new("prepare_checkout", "checkout_token")],
            CheckoutExpectation = CheckoutExpectation.RequiresDifferentFromPrevious,
            ResponseContainsAny = ["anticipo", "pago", "resumen", "link"]
        },
        new("booking1_confirm_payment", "__confirm_active_payment", []) { ResetBefore = false },
        new("booking1_verify_paid", "Ya pague el anticipo, verifica el pago", [])
        {
            ResetBefore = false,
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "check_availability", "prepare_checkout"],
            ResponseContainsAny = ["confirm", "reserva", "pago"]
        },
        new("post_reservation_change_time", "Quiero cambiar la hora de mi reserva a las 10:00", ["manage_reservation"])
        {
            ResetBefore = false,
            ExpectedStages = ["reservation_management"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "prepare_checkout"],
            ToolResultContains = [new("manage_reservation", "reservation_id")]
        },
        new("post_reservation_change_service", "Quiero cambiar el servicio de mi reserva a corte basico de adulto", ["manage_reservation"])
        {
            ResetBefore = false,
            ExpectedStages = ["reservation_management"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "prepare_checkout"],
            ToolResultContains = [new("manage_reservation", "OnHold"), new("manage_reservation", "escalated")]
        },
        new("booking2_catalog_after_management", "hola quiero agendar", ["get_service_catalog"])
        {
            ResetBefore = false,
            ExpectedStages = ["discovery"],
            ForbiddenTools = ["manage_reservation", "get_customer_reservations", "get_compatible_add_ons", "check_availability", "prepare_checkout"],
            ToolResultContains = [new("get_service_catalog", "## CATEGORIAS DE SERVICIOS")],
            ResponseContainsAll = ["bienvenido", "BARBER KIDS MENS", "Soy Luis Petit", "barbero profesional", "servicio", "interesado"],
            ResponseForbiddenContains = ["tu barbero profesional", "adicionales", "anticipo", "resumen", "hoy"]
        },
        new("booking2_service", "Quiero corte basico de nino", ["resolve_service_selection", "get_compatible_add_ons"])
        {
            ResetBefore = false,
            ExpectedStages = ["discovery", "add_ons"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("resolve_service_selection", "Corte basico de nino")],
            ResponseContainsAll = ["Quieres agregar alguno de estos adicionales o seguimos sin ellos"]
        },
        new("booking2_no_addons", "Sin adicionales", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["add_ons"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "check_availability", "prepare_checkout", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("set_fact", "add_ons"), new("set_fact", "ninguno")]
        },
        new("booking2_checkout", $"El {secondDate} a las 8:30. Soy Luis Critical, naci el 1990-01-01 y mi correo es luis.critical@example.com", ["set_fact", "check_availability", "prepare_checkout"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling", "finalization"],
            ExpectedToolOrder = ["set_fact", "check_availability", "prepare_checkout"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "manage_reservation", "get_customer_reservations"],
            ToolResultContains = [new("set_fact", "desired_date"), new("set_fact", "desired_time"), new("check_availability", "availability_checked"), new("prepare_checkout", "checkout_token")],
            ResponseContainsAny = ["anticipo", "pago", "resumen", "link"]
        },
        new("booking2_confirm_payment", "__confirm_active_payment", []) { ResetBefore = false }
    ];
}


static IReadOnlyList<ConsoleScenarioStep> GetMimosCriticalFlowSteps()
{
    var testDate = NextBusinessDate();

    return
    [
        new("mimos_greeting", "hola", [])
        {
            ExpectedStages = ["discovery"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "check_availability", "prepare_checkout", "create_reservation", "manage_reservation"],
            ResponseContainsAll = ["Mimo", "Baby Spa", "bebe"],
            ResponseContainsAny = ["ayudarte", "bienestar"],
            ResponseForbiddenContains = ["categoria", "catalogo", "servicio", "complementos", "anticipo", "resumen"]
        },
        new("mimos_capture_baby_context", "Mi bebe se llama Mia y tiene 8 meses", ["set_fact", "get_service_catalog"])
        {
            ResetBefore = false,
            ExpectedStages = ["discovery", "service_selection"],
            ForbiddenTools = ["get_compatible_add_ons", "check_availability", "prepare_checkout", "create_reservation", "manage_reservation"],
            ToolResultContains = [new("set_fact", "baby_name"), new("set_fact", "baby_age_months"), new("get_service_catalog", "## CATEGORIAS DE SERVICIOS")],
            ResponseContainsAny = ["categoria", "servicio", "experiencia"],
            ResponseForbiddenContains = ["complementos", "anticipo", "resumen"]
        },
        new("mimos_service_catalog", "quiero una experiencia de hidroterapia", ["get_service_catalog"])
        {
            ResetBefore = false,
            ExpectedStages = ["service_selection"],
            ForbiddenTools = ["get_compatible_add_ons", "check_availability", "prepare_checkout", "create_reservation", "manage_reservation"],
            ToolResultContains = [new("get_service_catalog", "## CATEGORIAS DE SERVICIOS")],
            ResponseContainsAny = ["hidro", "servicio", "experiencia"],
            ResponseForbiddenContains = ["complementos", "decoracion", "fotografia", "anticipo", "resumen"]
        },
        new("mimos_exact_service_addons", "Elijo Plan Aventuras Marinas", ["resolve_service_selection", "get_service_fulfillment", "get_compatible_add_ons"])
        {
            ResetBefore = false,
            ExpectedStages = ["service_selection", "addons_offering"],
            ExpectedToolOrder = ["resolve_service_selection", "get_service_fulfillment", "get_compatible_add_ons"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "create_reservation", "manage_reservation"],
            ToolResultContains = [new("get_service_fulfillment", "fulfillment_ready"), new("get_service_fulfillment", "reservation")],
            ResponseContainsAny = ["complemento", "decoracion", "fotografia", "continuar sin complementos"],
            ResponseForbiddenContains = ["resumen"]
        },
        new("mimos_no_addons", "seguimos sin complementos", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["addons_offering"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "check_availability", "prepare_checkout", "create_reservation", "manage_reservation"],
            ToolResultContains = [new("set_fact", "add_ons")],
            ResponseContainsAny = ["fecha", "cuando", "dia"],
            ResponseForbiddenContains = ["anticipo", "resumen"]
        },
        new("mimos_date_slots", $"Que horarios tienes para el {testDate}", ["set_fact", "check_availability"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ExpectedToolOrder = ["set_fact", "check_availability"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "prepare_checkout", "create_reservation", "manage_reservation"],
            ToolResultContains = [new("set_fact", "desired_date")],
            ResponseContainsAny = ["espacios", "horarios", "hora"]
        },
        new("mimos_pick_time", "A las 9:00", ["set_fact", "check_availability"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ExpectedToolOrder = ["set_fact", "check_availability"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "prepare_checkout", "create_reservation", "manage_reservation"],
            ToolResultContains = [new("set_fact", "desired_time"), new("check_availability", "availability_checked")],
            ResponseContainsAny = ["nombre", "nacimiento", "registro"]
        },
        new("mimos_checkout", "La registra Laura Perez y Mia nacio el 2025-11-15", ["set_fact", "prepare_checkout"])
        {
            ResetBefore = false,
            ExpectedStages = ["customer_data", "finalization"],
            ExpectedToolOrder = ["set_fact", "prepare_checkout"],
            ForbiddenTools = ["get_service_catalog", "get_compatible_add_ons", "manage_reservation"],
            ToolResultContains = [new("set_fact", "customer_name"), new("set_fact", "baby_birth_date"), new("prepare_checkout", "checkout_token")],
            ResponseContainsAny = ["resumen", "anticipo", "pago", "link"],
            ResponseForbiddenContains = ["Genero el resumen", "Quieres que genere"]
        }
    ];
}

static IReadOnlyList<ConsoleScenarioStep> GetAuralyCriticalFlowSteps()
{
    var testDate = NextBusinessDate();

    return
    [
        new("auraly_first_contact_sequence", "hola", ["send_message_sequence"])
        {
            ExpectedStages = ["discovery"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ResponseForbiddenContains = ["Que tipo de negocio tienes"]
        },
        new("auraly_capture_context_and_value", "Tengo un restaurante y quiero mejorar responder leads y agendar por WhatsApp", ["set_fact", "get_service_catalog"])
        {
            ResetBefore = false,
            ExpectedStages = ["discovery", "value_explanation"],
            ForbiddenTools = ["check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "business_type"), new("set_fact", "pain_point"), new("get_service_catalog", "Demo AURALY")],
            ResponseContainsAny = ["AURALY", "demo", "WhatsApp"]
        },
        new("auraly_ask_date", "quiero agendar la demo", ["resolve_service_selection"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ForbiddenTools = ["get_service_catalog", "prepare_checkout", "create_reservation"],
            ResponseContainsAny = ["fecha", "horarios", "demo"],
            ResponseForbiddenContains = ["servicio"]
        },
        new("auraly_date_slots", $"Para el {testDate}", ["set_fact", "check_availability"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ExpectedToolOrder = ["set_fact", "check_availability"],
            ForbiddenTools = ["get_service_catalog", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "desired_date")],
            ResponseContainsAny = ["espacios", "horarios", "hora"]
        },
        new("auraly_pick_time", "A las 14:00", ["set_fact", "check_availability"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ExpectedToolOrder = ["set_fact", "check_availability"],
            ForbiddenTools = ["get_service_catalog", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "desired_time"), new("check_availability", "availability_checked")],
            ResponseContainsAny = ["nombre", "empresa", "correo"]
        },
        new("auraly_customer_data", "Soy Ana Gomez de Restaurante Norte y mi correo es ana.gomez@example.com", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["customer_data"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "customer_name"), new("set_fact", "company_name"), new("set_fact", "customer_email")],
            ResponseContainsAny = ["confirma", "resumen", "demo"]
        },
        new("auraly_confirm", "confirmo", ["set_fact", "create_reservation"])
        {
            ResetBefore = false,
            ExpectedStages = ["confirmation", "reservation_creation"],
            ForbiddenTools = ["get_service_catalog", "prepare_checkout"],
            ToolResultContains = [new("set_fact", "customer_confirmed"), new("create_reservation", "reservation_id")],
            ResponseContainsAny = ["confirm", "demo", "AURALY"]
        }
    ];
}

static IReadOnlyList<ConsoleScenarioStep> GetRadaCriticalFlowSteps()
{
    var testDate = NextBusinessDate();

    return
    [
        new("rada_greeting", "hola", [])
        {
            ExpectedStages = ["discovery"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ResponseContainsAny = ["Rada Concept", "servicio"],
            ResponseForbiddenContains = ["Diseno Arquitectonico", "catalogo", "precio", "resumen"]
        },
        new("rada_project_catalog", "necesito diseno para remodelar una sala comercial", ["set_fact", "get_service_catalog"])
        {
            ResetBefore = false,
            ExpectedStages = ["discovery", "service_selection"],
            ForbiddenTools = ["check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "project_context"), new("get_service_catalog", "Diseno")],
            ResponseContainsAny = ["diseno", "remodelacion", "asesoria"]
        },
        new("rada_exact_service", "Elijo Asesoria en Diseno", ["resolve_service_selection"])
        {
            ResetBefore = false,
            ExpectedStages = ["service_selection"],
            ForbiddenTools = ["get_service_catalog", "prepare_checkout", "create_reservation"],
            ResponseContainsAny = ["fecha", "agenda", "asesoria"]
        },
        new("rada_date_slots", $"Que horarios tienes para el {testDate}", ["set_fact", "check_availability"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ExpectedToolOrder = ["set_fact", "check_availability"],
            ForbiddenTools = ["get_service_catalog", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "desired_date")],
            ResponseContainsAny = ["espacios", "horarios", "hora"]
        },
        new("rada_pick_time", "A las 10:00", ["set_fact", "check_availability"])
        {
            ResetBefore = false,
            ExpectedStages = ["scheduling"],
            ExpectedToolOrder = ["set_fact", "check_availability"],
            ForbiddenTools = ["get_service_catalog", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "desired_time"), new("check_availability", "availability_checked")],
            ResponseContainsAny = ["nombre", "celular"]
        },
        new("rada_customer_data", "Me llamo Carlos Ruiz y mi celular es 3001234567", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["customer_data"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "customer_name"), new("set_fact", "customer_phone")],
            ResponseContainsAny = ["resumen", "confirmas", "confirmacion"]
        },
        new("rada_confirm", "confirmo", ["set_fact", "create_reservation"])
        {
            ResetBefore = false,
            ExpectedStages = ["confirmation", "reservation_creation"],
            ForbiddenTools = ["get_service_catalog", "prepare_checkout"],
            ToolResultContains = [new("set_fact", "customer_confirmed"), new("create_reservation", "reservation_id")],
            ResponseContainsAny = ["agendada", "confirm"]
        }
    ];
}

static IReadOnlyList<ConsoleScenarioStep> GetSolorzanoCriticalFlowSteps()
{
    return
    [
        new("solorzano_greeting_catalog", "hola quiero ver vinos", ["search_products"])
        {
            ExpectedStages = ["discovery"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "create_reservation", "create_order"],
            ToolResultContains = [new("search_products", "Mango")],
            ResponseContainsAll = ["Que vino te gustaria degustar el dia de hoy"],
            ResponseForbiddenContains = ["precio", "$"]
        },
        new("solorzano_add_product", "quiero 2 mango 750", ["add_order_item"])
        {
            ResetBefore = false,
            ExpectedStages = ["discovery"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "create_reservation", "prepare_order_checkout", "create_order"],
            ToolResultContains = [new("add_order_item", "Mango 750ML")],
            ResponseContainsAll = ["Quieres agregar algo mas a la compra"]
        },
        new("solorzano_finalize_items", "no, asi esta bien", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["discovery"],
            ForbiddenTools = ["search_products", "prepare_order_checkout", "create_order"],
            ToolResultContains = [new("set_fact", "order_finalized")],
            ResponseContainsAny = ["Direccion", "Celular", "Nombre"]
        },
        new("solorzano_delivery_data", "Direccion Calle 10 #20-30, celular 3001234567, recibe Carlos Perez", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["order_data"],
            ForbiddenTools = ["search_products", "prepare_order_checkout", "create_order"],
            ToolResultContains = [new("set_fact", "delivery_address"), new("set_fact", "delivery_phone"), new("set_fact", "customer_name")],
            ResponseContainsAny = ["efectivo", "transferencia"]
        },
        new("solorzano_payment_summary", "pago en efectivo", ["set_fact", "prepare_order_checkout"])
        {
            ResetBefore = false,
            ExpectedStages = ["payment_method", "summary"],
            ExpectedToolOrder = ["set_fact", "prepare_order_checkout"],
            ForbiddenTools = ["search_products", "get_service_catalog", "check_availability", "prepare_checkout", "create_reservation", "create_order"],
            ToolResultContains = [new("set_fact", "payment_method"), new("prepare_order_checkout", "order_checkout_presented")],
            ResponseContainsAny = ["resumen", "total", "envio", "confirm"]
        },
        new("solorzano_confirm_order", "confirmo el pedido", ["create_order"])
        {
            ResetBefore = false,
            ExpectedStages = ["order_confirmation"],
            ForbiddenTools = ["search_products", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("create_order", "order_id")],
            ResponseContainsAny = ["pedido", "confirm"]
        }
    ];
}

static string NextBusinessDate()
{
    var date = DateTime.Today.AddDays(3650 + (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1200));
    while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        date = date.AddDays(1);

    return date.ToString("yyyy-MM-dd");
}

static bool ResponseContains(string? response, string expected) =>
    !string.IsNullOrWhiteSpace(response)
    && NormalizeConsoleText(response).Contains(NormalizeConsoleText(expected), StringComparison.Ordinal);

static string NormalizeConsoleText(string value)
{
    var normalized = value.Trim().ToLowerInvariant()
        .Normalize(System.Text.NormalizationForm.FormD);
    return new string(normalized
        .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
            != System.Globalization.UnicodeCategory.NonSpacingMark)
        .ToArray());
}

static bool TraceContainsTool(IReadOnlyList<AgentTurnTraceEntry> trace, string toolName) =>
    trace.Any(entry => string.Equals(entry.ToolName, toolName, StringComparison.OrdinalIgnoreCase)
        && !IsSkippedStaleToolTrace(entry));

static bool IsSkippedStaleToolTrace(AgentTurnTraceEntry entry) =>
    !string.IsNullOrWhiteSpace(entry.ToolResultJson)
    && (entry.ToolResultJson.Contains("stale_tool_batch_stage_changed", StringComparison.OrdinalIgnoreCase)
        || entry.ToolResultJson.Contains("stale_fact_invalidated_by_previous_tool", StringComparison.OrdinalIgnoreCase)
        || entry.ToolResultJson.Contains("fact_change_requires_capture", StringComparison.OrdinalIgnoreCase));

static bool TraceContainsStage(IReadOnlyList<AgentTurnTraceEntry> trace, string stageId) =>
    trace.Any(entry => string.Equals(entry.StageId, stageId, StringComparison.OrdinalIgnoreCase));

static bool TraceToolResultContains(IReadOnlyList<AgentTurnTraceEntry> trace, string toolName, string expected) =>
    trace.Any(entry => string.Equals(entry.ToolName, toolName, StringComparison.OrdinalIgnoreCase)
        && !IsSkippedStaleToolTrace(entry)
        && !string.IsNullOrWhiteSpace(entry.ToolResultJson)
        && entry.ToolResultJson.Contains(expected, StringComparison.OrdinalIgnoreCase));

static ConsoleCheckoutSnapshot? ExtractLastCheckout(IReadOnlyList<AgentTurnTraceEntry> trace)
{
    foreach (var entry in trace.Reverse())
    {
        if (!string.Equals(entry.ToolName, "prepare_checkout", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(entry.ToolResultJson))
            continue;

        using var doc = JsonDocument.Parse(entry.ToolResultJson);
        if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            continue;
        if (!doc.RootElement.TryGetProperty("data", out var data))
            continue;
        if (!data.TryGetProperty("payment_transaction_id", out var paymentIdElement))
            continue;

        var paymentId = paymentIdElement.GetString();
        if (string.IsNullOrWhiteSpace(paymentId))
            continue;

        var paymentUrl = data.TryGetProperty("payment_url", out var urlElement)
            ? urlElement.GetString()
            : null;
        return new ConsoleCheckoutSnapshot(paymentId, paymentUrl);
    }

    return null;
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

static IReadOnlyList<ConsoleScenarioStep> GetCjDistribucionesCriticalFlowSteps()
{
    return
    [
        new("cj_identification", "hola", [])
        {
            ExpectedStages = ["customer_name"],
            ForbiddenTools = ["search_products", "prepare_order_checkout", "create_order", "get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ResponseContainsAll = ["CJ Distribuciones", "nombre"],
            ResponseForbiddenContains = ["catalogo", "precio", "$", "total"]
        },
        new("cj_profile", "Soy Surtimax La 15", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["customer_name", "customer_type"],
            ForbiddenTools = ["search_products", "prepare_order_checkout", "create_order", "get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "customer_name")],
            ResponseContainsAny = ["Hogar", "Tienda", "Restaurante", "Comida rapida", "Distribuidor"]
        },
        new("cj_profile_natural_language", "Tengo una tienda y minimercado", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["customer_type"],
            ForbiddenTools = ["prepare_order_checkout", "create_order", "get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "customer_type")],
            ResponseContainsAny = ["productos", "catalogo", "necesitas", "preparar"]
        },
        new("cj_product_list", "Necesito 2 pechugas, 1 papa 2.5 y 1 quesillo de 20 lonchas", ["search_products", "add_order_item"])
        {
            ResetBefore = false,
            ExpectedStages = ["product_selection"],
            ForbiddenTools = ["get_service_catalog", "check_availability", "prepare_checkout", "create_reservation", "prepare_order_checkout", "create_order"],
            ToolResultContains = [new("search_products", "Pechuga"), new("search_products", "Papa a la francesa 2.5 kg"), new("search_products", "Quesillo 20 lonchas")],
            ResponseContainsAny = ["pechuga", "papa", "quesillo", "agreg"],
            ResponseForbiddenContains = ["Se muestran referencias", "placeholder"]
        },
        new("cj_finalize_cart_review", "eso es todo, dame el total", ["set_fact", "get_order_draft"])
        {
            ResetBefore = false,
            ExpectedStages = ["product_selection", "cart_review"],
            ForbiddenTools = ["search_products", "prepare_order_checkout", "create_order", "get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "order_finalized")],
            ResponseContainsAny = ["pedido", "total", "correcto", "modificar"]
        },
        new("cj_confirm_cart_delivery_method", "esta correcto", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["cart_review", "order_data"],
            ForbiddenTools = ["search_products", "prepare_order_checkout", "create_order", "get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "cart_review_confirmed")],
            ResponseContainsAny = ["recoger", "recogida", "domicilio"]
        },
        new("cj_delivery_data", "A domicilio en Calle 10 #20-30 barrio Centro, celular 3001234567", ["set_fact"])
        {
            ResetBefore = false,
            ExpectedStages = ["order_data"],
            ForbiddenTools = ["search_products", "prepare_order_checkout", "create_order", "get_service_catalog", "check_availability", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("set_fact", "delivery_method"), new("set_fact", "delivery_address"), new("set_fact", "delivery_phone")],
            ResponseContainsAny = ["efectivo", "transferencia"]
        },
        new("cj_payment_summary", "pago en efectivo", ["set_fact", "prepare_order_checkout"])
        {
            ResetBefore = false,
            ExpectedStages = ["payment_method", "summary"],
            ExpectedToolOrder = ["set_fact", "prepare_order_checkout"],
            ForbiddenTools = ["search_products", "get_service_catalog", "check_availability", "prepare_checkout", "create_reservation", "create_order"],
            ToolResultContains = [new("set_fact", "payment_method"), new("prepare_order_checkout", "order_checkout_presented")],
            ResponseContainsAny = ["resumen", "total", "envio", "confirm"]
        },
        new("cj_confirm_order", "confirmo el pedido", ["create_order"])
        {
            ResetBefore = false,
            ExpectedStages = ["order_confirmation"],
            ForbiddenTools = ["search_products", "prepare_checkout", "create_reservation"],
            ToolResultContains = [new("create_order", "order_id")],
            ResponseContainsAny = ["pedido", "confirm"]
        }
    ];
}

static string CreateTestUserPhone()

{

    var configuredPhone = Environment.GetEnvironmentVariable("TALKIO_CONSOLE_PHONE");

    if (!string.IsNullOrWhiteSpace(configuredPhone))

        return configuredPhone.Trim();



    return CreateFreshTestUserPhone();

}

static string CreateFreshTestUserPhone() =>
    $"+1555{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 10000000:0000000}{Random.Shared.Next(0, 100):00}";

static ConsoleAgentOptions? ResolveScenarioAgent(string? scenario) => scenario?.ToLowerInvariant() switch
{
    "mimos-critical-flow" => new ConsoleAgentOptions(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Mimos Baby Spa - Valledupar",
        Guid.Parse("7105A9D5-D4E4-4BBA-9F3A-DBB34E0B1B86"),
        "Mimi Bot",
        "Mimi",
        ["Mimi Bot", "Mimo Bot"]),
    "auraly-critical-flow" => new ConsoleAgentOptions(
        Guid.Parse("A0A10000-0000-0000-0000-000000000001"),
        "AURALY",
        Guid.Parse("A0A10000-0000-0000-0000-000000000002"),
        "Aly",
        "Aly",
        ["Aly"]),
    "rada-critical-flow" => new ConsoleAgentOptions(
        Guid.Parse("AADA0000-0000-0000-0000-000000000001"),
        "Rada Concept",
        Guid.Parse("AADA0000-0000-0000-0000-000000000002"),
        "Asistente Rada Concept",
        "Rada",
        ["Asistente Rada Concept"]),
    "solorzano-critical-flow" => new ConsoleAgentOptions(
        Guid.Parse("FCEE3BA9-E6BF-43E2-8C1A-560CB724688B"),
        "Vinos Artesanales Solorzano",
        Guid.Parse("B0EE3BA9-E6BF-43E2-8C1A-560CB724688B"),
        "Camila",
        "Camila",
        ["Camila"]),
    "cjdistribuciones-critical-flow" => new ConsoleAgentOptions(
        Guid.Parse("C1D15A00-0000-0000-0000-000000000010"),
        "CJ Distribuciones",
        Guid.Parse("C1D15A00-0000-0000-0000-000000000020"),
        "Asistente CJ Distribuciones",
        "CJ",
        ["Asistente CJ Distribuciones"]),
    _ => null
};

internal sealed record ConsoleScenarioStep(string Id, string UserMessage, IReadOnlyList<string> ExpectedTools)
{
    public IReadOnlyList<string> ForbiddenTools { get; init; } = [];
    public IReadOnlyList<string> ExpectedToolOrder { get; init; } = [];
    public IReadOnlyList<string> ExpectedStages { get; init; } = [];
    public IReadOnlyList<string> ForbiddenStages { get; init; } = [];
    public IReadOnlyList<string> ResponseContainsAny { get; init; } = [];
    public IReadOnlyList<string> ResponseContainsAll { get; init; } = [];
    public IReadOnlyList<string> ResponseForbiddenContains { get; init; } = [];
    public IReadOnlyList<ConsoleToolResultExpectation> ToolResultContains { get; init; } = [];
    public bool ResetBefore { get; init; } = true;
    public CheckoutExpectation CheckoutExpectation { get; init; } = CheckoutExpectation.None;
}
internal sealed record ConsoleToolResultExpectation(string ToolName, string Text);
internal enum CheckoutExpectation
{
    None,
    RequiresSameAsPrevious,
    RequiresDifferentFromPrevious
}

internal sealed class ConsoleCheckoutState
{
    public string? LastPaymentTransactionId { get; set; }
    public string? LastPaymentUrl { get; set; }
}

internal sealed record ConsoleCheckoutSnapshot(string PaymentTransactionId, string? PaymentUrl);
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
