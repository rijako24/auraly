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

using MimosBabySpa.Application.Agents.Planning;

using MimosBabySpa.Application.Billing;

using MimosBabySpa.Application.Agents.Composition;

using MimosBabySpa.Application.Agents.Facts;

using MimosBabySpa.Application.Agents.Gating;

using MimosBabySpa.Application.Agents.Runtime;

using MimosBabySpa.Application.Agents.Operations.Support;

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

if (args is ["product-resolution-smoke"])
{
    Environment.ExitCode = await MimosBabySpa.Console.ProductResolutionConsoleScenario.RunAsync();
    return;
}


if (args is ["payment-approval-smoke"])

{

    Environment.ExitCode = await MimosBabySpa.Console.PaymentApprovalConsoleScenario.RunAsync();

    return;

}



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
services.AddScoped<ICommerceCustomerResolver, CommerceCustomerResolver>();

services.AddScoped<IProductLookupService>(provider => (IProductLookupService)provider.GetRequiredService<ICommerceService>());

services.AddScoped<ICatalogRecommendationService, CatalogRecommendationService>();

services.AddScoped<ICommerceAdapter, LocalCommerceAdapter>();

services.AddHttpClient<SiigoCommerceAdapter>();

services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<SiigoCommerceAdapter>());

services.AddHttpClient<MantisCommerceAdapter>();

services.AddScoped<ICommerceAdapter>(sp => sp.GetRequiredService<MantisCommerceAdapter>());

services.AddScoped<ICommerceAdapterFactory, CommerceAdapterFactory>();

// OpenAI Clients

services.Configure<OpenAITextModelOptions>(configuration.GetSection(OpenAITextModelOptions.SectionName));

services.Configure<OpenAIAudioModelOptions>(configuration.GetSection(OpenAIAudioModelOptions.SectionName));

services.AddKeyedSingleton<AzureOpenAIClient>("Text", (sp, _) =>

{

    var opts = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;

    if (string.IsNullOrEmpty(opts.Endpoint) || string.IsNullOrEmpty(opts.ApiKey))

        throw new InvalidOperationException("OpenAI:TextModel:Endpoint y ApiKey deben estar configurados");

    return new AzureOpenAIClient(new Uri(opts.Endpoint), new System.ClientModel.ApiKeyCredential(opts.ApiKey));

});

services.AddKeyedSingleton<AzureOpenAIClient>("Audio", (sp, _) =>

{

    var opts = sp.GetRequiredService<IOptions<OpenAIAudioModelOptions>>().Value;

    if (string.IsNullOrEmpty(opts.Endpoint) || string.IsNullOrEmpty(opts.ApiKey))

        throw new InvalidOperationException("OpenAI:AudioModel:Endpoint y ApiKey deben estar configurados");

    return new AzureOpenAIClient(new Uri(opts.Endpoint), new System.ClientModel.ApiKeyCredential(opts.ApiKey));

});

// Supporting Infrastructure

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

services.AddScoped<AgentConfigProviderAccessor>(sp => () => sp.GetRequiredService<IAgentConfigProvider>());
services.AddScoped<ExternalEscalationOutcomePublisherAccessor>(sp => () => sp.GetRequiredService<IExternalEscalationOutcomePublisher>());
services.AddScoped<IExternalEscalationService, ExternalEscalationService>();

        services.AddScoped<IExternalEscalationOutcomePublisher, ExternalEscalationOutcomePublisher>();

services.AddHttpClient();

services.AddHttpClient<GoogleCalendarService>(c => c.Timeout = TimeSpan.FromSeconds(30));

services.AddScoped<ICalendarService, GoogleCalendarService>();

// Business Rules Engine

services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();

// Deterministic Agent Engine

services.AddScoped<MimosBabySpa.Application.LLM.IChatClient>(sp =>

{

    var textClient = sp.GetRequiredKeyedService<AzureOpenAIClient>("Text");

    var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;

    var logger = sp.GetRequiredService<ILogger<AzureOpenAIChatClient>>();

    return new AzureOpenAIChatClient(textClient, textOptions.DeploymentName, logger);

});

services.AddScoped<IAgentConfigProvider, AgentConfigProvider>();

// Hydrator: plugin model

services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelPhoneResolver>();

services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelEmailResolver>();

services.AddSingleton<IFactHydrator, FactHydrator>();

services.AddScoped<IConversationVerificationService, ConversationVerificationService>();

services.AddScoped<IOperatingHoursTurnPolicy, OperatingHoursTurnPolicy>();

services.AddSingleton<TurnPlanValidator>();

services.AddScoped<ITurnPlanningContextEnricher, CommerceCartPlanningContextEnricher>();
services.AddScoped<ITurnPlanner, LlmTurnPlanner>();

services.AddScoped<TurnPlanPilotRunner>();

services.AddScoped<IAgentTemplateResolver, AgentTemplateResolver>();

services.AddScoped<ITemplateRenderer, PromptTemplateRenderer>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Availability.CheckAvailabilityOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetCompatibleAddOnsOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetServiceFulfillmentOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.ResolveServiceSelectionOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Catalog.GetServiceCatalogOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.ApplyOrderChangesOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.SearchProductsOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.SearchRecipesOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.Reservation.IReservationCheckoutPreparationService, MimosBabySpa.Application.Agents.Operations.Reservation.ReservationCheckoutPreparationService>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.PrepareReservationCheckoutOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.CreateCommerceOrderOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.PrepareCommerceCheckoutOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.Reservation.IReservationCreationService, MimosBabySpa.Application.Agents.Operations.Reservation.ReservationCreationService>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.ManageReservationOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.CreateReservationOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Reservation.ListCustomerReservationsOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Commerce.GetOrderDraftOperation>();
services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Conversation.GetKnownFactsOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Escalation.RequestHumanEscalationOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IAgentOperation, MimosBabySpa.Application.Agents.Operations.Conversation.ResetConversationRequestOperation>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.AgentOperationRegistry>();

MimosBabySpa.Application.Agents.Operations.Internal.InternalOperationRegistration.AddInternalAgentOperations(services);

services.AddSingleton<MimosBabySpa.Application.Agents.Facts.FactMutationBatchProcessor>();

services.AddSingleton<MimosBabySpa.Application.Agents.Runtime.IDeterministicFlowSelector, MimosBabySpa.Application.Agents.Runtime.DeterministicFlowSelector>();

services.AddScoped<MimosBabySpa.Application.Commerce.ICartProductResolver, MimosBabySpa.Application.Commerce.CommerceCartProductResolver>();
services.AddScoped<MimosBabySpa.Application.Commerce.IProductCandidateRetriever, MimosBabySpa.Application.Commerce.LocalProductCandidateRetriever>();
services.AddScoped<MimosBabySpa.Application.Commerce.IProductAliasService, MimosBabySpa.Application.Commerce.ProductAliasService>();

services.AddScoped<MimosBabySpa.Application.Commerce.ICartMutationStore, MimosBabySpa.Application.Commerce.CommerceCartMutationStore>();

services.AddScoped<MimosBabySpa.Application.Commerce.CartCommandBatchProcessor>();

services.AddScoped<MimosBabySpa.Application.Agents.Configuration.AgentConfigurationCompiler>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.StageConditionEvaluator>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.OperationArgumentBinder>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicStageExecutor>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicStageTransitionResolver>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.DeterministicTurnCoordinator>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IDeterministicResponseRenderer, MimosBabySpa.Application.Agents.Runtime.DeterministicResponseRenderer>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IOperationEventContextResolver, MimosBabySpa.Application.Agents.Runtime.ReservationCreatedOperationEventContextResolver>();
services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IOperationEventContextResolver, MimosBabySpa.Application.Agents.Runtime.OrderCreatedOperationEventContextResolver>();

services.AddScoped<MimosBabySpa.Application.Agents.Runtime.IDeterministicTurnEffectProcessor, MimosBabySpa.Application.Agents.Runtime.DeterministicTurnEffectProcessor>();

services.AddScoped<MimosBabySpa.Application.Agents.Operations.IOperationPresentationComposer, MimosBabySpa.Application.Agents.Operations.OperationPresentationComposer>();

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

var consoleAgent = ResolveRequestedConsoleAgent(args) ?? ConsoleAgentOptions.FromEnvironment();

var traceEnabled = IsTraceEnabled(args);

if (TurnPlanPilotRunner.IsRequested(args))
{
    await using var pilotScope = serviceProvider.CreateAsyncScope();
    var pilot = pilotScope.ServiceProvider.GetRequiredService<TurnPlanPilotRunner>();
    Environment.ExitCode = await pilot.RunAsync(consoleAgent.AgentId, args);
    return;
}

Console.WriteLine("========================================================");

Console.WriteLine($"  {consoleAgent.BusinessName} - Simulador de Motor Deterministico");

Console.WriteLine("========================================================");

Console.WriteLine();

Console.WriteLine($"  Negocio : {consoleAgent.BusinessName} ({consoleAgent.BusinessId})");

Console.WriteLine($"  Agente  : {consoleAgent.AgentName}");

Console.WriteLine("  Escribe  'exit' para salir");

Console.WriteLine("  Escribe  'reset' para reiniciar la sesion");

Console.WriteLine("  Usa     --trace para ver TurnPlan, operaciones y respuestas del LLM");

Console.WriteLine("  Usa     pilot-seed-turn-plan para inspeccionar una extraccion real");
Console.WriteLine("  Usa     eval-seed-extractor para ejecutar una suite de extraccion");
Console.WriteLine("  Usa     mimos, luis o cj para seleccionar el agente interactivo");

Console.WriteLine();

// Simula el telefono del cliente (clave de sesion).

var userPhone = CreateTestUserPhone();

var customerName = Environment.GetEnvironmentVariable("TALKIO_CONSOLE_CUSTOMER_NAME");
var recipientPhoneNumberId = Environment.GetEnvironmentVariable(
    "TALKIO_CONSOLE_RECIPIENT_PHONE_NUMBER_ID")?.Trim();
var singleTurnMode = args.Any(value =>
    value.Equals("single-turn", StringComparison.OrdinalIgnoreCase));

string? singleTurnMessage = null;
if (singleTurnMode)
{
    var encodedMessage = Environment.GetEnvironmentVariable("TALKIO_CONSOLE_MESSAGE_BASE64");
    if (string.IsNullOrWhiteSpace(encodedMessage))
    {
        Console.Error.WriteLine(
            "TALKIO_CONSOLE_MESSAGE_BASE64 is required in single-turn mode.");
        Environment.ExitCode = 2;
        return;
    }

    try
    {
        singleTurnMessage = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(encodedMessage));
    }
    catch (FormatException)
    {
        Console.Error.WriteLine("TALKIO_CONSOLE_MESSAGE_BASE64 is not valid Base64.");
        Environment.ExitCode = 2;
        return;
    }
}


while (true)

{

    string? input;
    if (singleTurnMode)
    {
        input = singleTurnMessage;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Tu: [mensaje multilinea de prueba]");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Tu: ");
        Console.ResetColor();
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
            var confirmed = await ConfirmLatestConversationPaymentAsync(
                scope.ServiceProvider,
                conversation.ConversationId,
                null,
                0,
                CancellationToken.None);
            if (!confirmed)
                Environment.ExitCode = 1;
            Console.WriteLine();
            continue;
        }

        var result = await agentService.ProcessMessageAsync(
            agentEntity.AgentId,
            conversation.ConversationId,
            input,
            userPhone,
            cancellationToken: CancellationToken.None,
            inboundMetadata: string.IsNullOrWhiteSpace(recipientPhoneNumberId)
                ? null
                : new AgentInboundMetadata(
                    null,
                    null,
                    null,
                    RecipientPhoneNumberId: recipientPhoneNumberId));

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

        if (singleTurnMode)
        {
            Environment.ExitCode = result.Success ? 0 : 1;
            break;
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

        if (singleTurnMode)
        {
            Environment.ExitCode = 1;
            break;
        }

    }

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

static ConsoleAgentOptions? ResolveRequestedConsoleAgent(string[] args)

{

    foreach (var arg in args)

    {

        var normalized = arg.Trim()

            .TrimStart('-')

            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)

            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)

            .ToLowerInvariant();

        if (normalized is "cj" or "cjdistribuciones")

            return ConsoleAgentOptions.CjDistribuciones();

        if (normalized is "luis" or "luispetit")

            return ConsoleAgentOptions.LuisPetit();

        if (normalized is "mimos" or "mimi" or "mimobot")
            return ConsoleAgentOptions.Mimos();

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

    PrintTurnSummary(trace);

    Console.ResetColor();

    foreach (var entry in trace)

    {

        Console.ForegroundColor = ConsoleColor.DarkYellow;

        Console.WriteLine($"[{entry.Kind}] iter={entry.Iteration} stage={entry.StageId ?? "(sin etapa)"}");

        Console.ResetColor();

        if (entry.EnabledOperations.Count > 0)

            Console.WriteLine($"operations: {string.Join(", ", entry.EnabledOperations)}");

        if (!string.IsNullOrWhiteSpace(entry.FinishReason))

            Console.WriteLine($"finish: {entry.FinishReason}");

        if (entry.OperationCalls.Count > 0)

        {

            Console.WriteLine("operation_calls:");

            foreach (var operationCall in entry.OperationCalls)

            {

                Console.WriteLine($"- {operationCall.OperationId}({operationCall.ArgumentsJson}) id={operationCall.Id}");

            }

        }

        if (!string.IsNullOrWhiteSpace(entry.OperationId))

        {

            Console.WriteLine($"action: {entry.ActionId ?? "(sin id)"}");
            Console.WriteLine($"operation: {entry.OperationId}({entry.OperationArgumentsJson})");

            Console.WriteLine("operation_result_visible_para_llm:");

            Console.WriteLine(PrettyJson(entry.OperationOutcomeJson, jsonOptions));

        }

        if (!string.IsNullOrWhiteSpace(entry.Content))

        {

            Console.WriteLine(entry.Kind == "turn_plan" ? "turn_plan:" : "respuesta_llm:");
            Console.WriteLine(entry.Kind == "turn_plan"
                ? PrettyJson(entry.Content, jsonOptions)
                : entry.Content);

        }

        Console.WriteLine();

    }

}

static void PrintTurnSummary(IReadOnlyList<AgentTurnTraceEntry> trace)
{
    var planEntry = trace.FirstOrDefault(entry => entry.Kind == "turn_plan" && !string.IsNullOrWhiteSpace(entry.Content));
    Console.WriteLine("facts_extraidos:");
    if (planEntry is null)
    {
        Console.WriteLine("- (sin TurnPlan)");
    }
    else
    {
        using var document = JsonDocument.Parse(planEntry.Content!);
        var plan = document.RootElement.GetProperty("plan");
        var facts = plan.GetProperty("Facts");
        if (facts.GetArrayLength() == 0)
            Console.WriteLine("- (ninguno)");
        foreach (var fact in facts.EnumerateArray())
            Console.WriteLine($"- {fact.GetProperty("Key").GetString()} {fact.GetProperty("Operation").GetString()} = {fact.GetProperty("Value")}");

        Console.WriteLine("senales_extraidas:");
        var signals = plan.GetProperty("Signals");
        if (signals.GetArrayLength() == 0)
            Console.WriteLine("- (ninguna)");
        foreach (var signal in signals.EnumerateArray())
            Console.WriteLine($"- {signal.GetProperty("Type").GetString()} = {signal.GetProperty("Value")}");
    }

    Console.WriteLine("acciones_ejecutadas:");
    var executed = trace.Where(entry => entry.Kind == "operation").ToList();
    if (executed.Count == 0)
        Console.WriteLine("- (ninguna)");
    foreach (var entry in executed)
        Console.WriteLine($"- {entry.ActionId ?? "(sin id)"}: {entry.OperationId}");

    Console.WriteLine("acciones_omitidas:");
    var skipped = trace.Where(entry => entry.Kind == "operation_skipped").ToList();
    if (skipped.Count == 0)
        Console.WriteLine("- (ninguna)");
    foreach (var entry in skipped)
        Console.WriteLine($"- {entry.ActionId ?? "(sin id)"}: {entry.OperationId}");
    Console.WriteLine();
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

    return CreateFreshTestUserPhone();

}

static string CreateFreshTestUserPhone() =>

    $"+1555{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 10000000:0000000}{Random.Shared.Next(0, 100):00}";

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

        var defaults = CjDistribuciones();

        var businessId = GetGuid(

            "TALKIO_CONSOLE_BUSINESS_ID",

            defaults.BusinessId.ToString());

        var agentId = GetGuid(

            "TALKIO_CONSOLE_AGENT_ID",

            defaults.AgentId.ToString());

        var agentName = GetText("TALKIO_CONSOLE_AGENT_NAME", defaults.AgentName);

        return new ConsoleAgentOptions(

            businessId,

            GetText("TALKIO_CONSOLE_BUSINESS_NAME", defaults.BusinessName),

            agentId,

            agentName,

            GetText("TALKIO_CONSOLE_AGENT_DISPLAY_NAME", agentName),

            GetList("TALKIO_CONSOLE_AGENT_ALIASES", string.Join(';', defaults.AgentAliases)));

    }

    public static ConsoleAgentOptions Mimos() => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Mimo's Baby Spa",
        Guid.Empty,
        "Mimi Bot",
        "Mimi",
        ["Mimi Bot", "Mimo Bot"]);

    public static ConsoleAgentOptions LuisPetit() => new(

        Guid.Parse("BABA0000-0000-0000-0000-000000000001"),

        "Luis Petit Profesional Barber",

        Guid.Parse("BABA0000-0000-0000-0000-000000000002"),

        "Luis",

        "Luis",

        ["Luis Petit"]);

    public static ConsoleAgentOptions CjDistribuciones() => new(

        Guid.Parse("C1D15A00-0000-0000-0000-000000000010"),

        "CJ Distribuciones",

        Guid.Parse("C1D15A00-0000-0000-0000-000000000020"),

        "Asistente CJ Distribuciones",

        "CJ",

        ["Asistente CJ Distribuciones", "CJ"]);

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
