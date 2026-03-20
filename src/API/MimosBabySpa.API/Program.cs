using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.GenericFlow;
using MimosBabySpa.Application.GenericFlow.Actions;
using MimosBabySpa.Application.GenericFlow.Handlers;
using MimosBabySpa.Application.GenericFlow.Services;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Configuration;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Repositories;
using MimosBabySpa.Infrastructure.Services;
using Azure.Storage.Blobs;
using Azure.AI.OpenAI;
using System.Net.Http;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddMemoryCache();
        services.AddScoped<CachedBusinessContextProvider>();
        services.AddScoped<IPromptProvider, SystemPromptProvider>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();

        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<ILeadService, LeadService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IBusinessIdentificationService, BusinessIdentificationService>();
        services.AddScoped<IBusinessConfigurationService, BusinessConfigurationService>();
        services.AddScoped<IWhatsAppWebhookParserService, WhatsAppWebhookParserService>();

        services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();

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

        services.AddScoped<ILLMAdapter>(sp =>
        {
            var textClient = sp.GetRequiredKeyedService<OpenAIClient>("Text");
            var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<AzureOpenAIAdapter>>();

            return new AzureOpenAIAdapter(textClient, textOptions.DeploymentName, logger);
        });

        // Transcripción de audio en WhatsApp (IAIService); no forma parte del orquestador legacy.
        services.AddScoped<IAIService>(sp =>
        {
            var textClient = sp.GetRequiredKeyedService<OpenAIClient>("Text");
            var audioClient = sp.GetRequiredKeyedService<OpenAIClient>("Audio");
            var textOptions = sp.GetRequiredService<IOptions<OpenAITextModelOptions>>().Value;
            var audioOptions = sp.GetRequiredService<IOptions<OpenAIAudioModelOptions>>().Value;
            var systemPromptProvider = sp.GetRequiredService<IPromptProvider>();
            var cachedContextProvider = sp.GetRequiredService<CachedBusinessContextProvider>();
            var logger = sp.GetRequiredService<ILogger<AIService>>();

            return new AIService(textClient, audioClient, textOptions.DeploymentName, audioOptions.DeploymentName,
                systemPromptProvider, cachedContextProvider, logger);
        });

        services.AddScoped<IPaymentLinkService, WompiPaymentLinkService>();
        services.AddScoped<IPaymentConfirmationHandler, PaymentConfirmationHandler>();
        services.AddSingleton<IWompiWebhookSignatureValidator, WompiWebhookSignatureValidator>();

        services.AddScoped<IEscalationNotifier, EscalationNotifier>();
        services.AddScoped<AdminActionLinkService>();
        services.AddScoped<IAdminActionLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
        services.AddScoped<IReleaseLinkService>(sp => sp.GetRequiredService<AdminActionLinkService>());
        services.AddScoped<IConversationReleaseService, ConversationReleaseService>();

        services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();

        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        services.AddScoped<IFlowExecutionStateRepository, FlowExecutionStateRepository>();
        services.AddScoped<IKnowledgeSourceRepository, KnowledgeSourceRepository>();

        services.AddScoped<TemplateResolver>();
        services.AddScoped<KnowledgeSourceRenderer>();
        services.AddScoped<FlowPromptBuilder>();
        services.AddScoped<FlowExtractionService>();
        services.AddScoped<FlowStateManager>();
        services.AddScoped<ServiceNameResolver>();
        services.AddScoped<ReservationPricingResolver>();
        services.AddScoped<ICatalogContentGenerator, CatalogContentGenerator>();

        services.AddScoped<INodeHandler, StartNodeHandler>();
        services.AddScoped<INodeHandler, EndNodeHandler>();
        services.AddScoped<INodeHandler, CollectFieldsNodeHandler>();
        services.AddScoped<INodeHandler, ActionNodeHandler>();
        services.AddScoped<INodeHandler, LLMClassifyNodeHandler>();
        services.AddScoped<INodeHandler, IntentionRouterNodeHandler>();
        services.AddScoped<INodeHandler, GenerateResponseNodeHandler>();
        services.AddScoped<INodeHandler, WaitForEventNodeHandler>();
        services.AddScoped<INodeHandler, EscalateNodeHandler>();
        services.AddScoped<INodeHandler, AgentNodeHandler>();

        services.AddScoped<IFlowAction, CheckAvailabilityAction>();
        services.AddScoped<IFlowAction, ResolvePricingAction>();
        services.AddScoped<IFlowAction, CreateReservationAction>();
        services.AddScoped<IFlowAction, GeneratePaymentLinkAction>();
        services.AddScoped<IFlowAction, VerifyPaymentAction>();
        services.AddScoped<IFlowAction, SetupRescheduleAction>();
        services.AddScoped<IFlowAction, RescheduleAction>();
        services.AddScoped<IFlowAction, SuspendAction>();

        services.AddScoped<IFlowOrchestrationService, FlowOrchestrationService>();

        services.AddHttpClient();
        services.Configure<WhatsAppWebhookOptions>(
            configuration.GetSection(WhatsAppWebhookOptions.SectionName));
        services.AddScoped<IWhatsAppCredentialResolver, WhatsAppCredentialResolver>();
        services.AddScoped<IWhatsAppService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var resolver = sp.GetRequiredService<IWhatsAppCredentialResolver>();
            var logger = sp.GetRequiredService<ILogger<WhatsAppService>>();
            var webhookOptions = sp.GetRequiredService<IOptions<WhatsAppWebhookOptions>>();
            return new WhatsAppService(httpClient, resolver, logger, webhookOptions);
        });

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config["AzureWebJobsStorage"]
                ?? throw new InvalidOperationException("AzureWebJobsStorage debe estar configurado");

            return new BlobServiceClient(connectionString);
        });

        services.AddScoped<IBlobStorageService>(sp =>
        {
            var client = sp.GetRequiredService<BlobServiceClient>();
            return new BlobStorageService(client, sp.GetRequiredService<ILogger<BlobStorageService>>());
        });

        services.AddScoped<IIntegrationsConfigProvider, IntegrationsConfigProvider>();

        services.Configure<ReleaseLinkSettings>(
            configuration.GetSection(ReleaseLinkSettings.SectionName));

        services.AddHttpClient<GoogleCalendarService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ICalendarService, GoogleCalendarService>();
    })
    .Build();

host.Run();
