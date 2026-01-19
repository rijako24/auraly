using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Repositories;
using MimosBabySpa.Infrastructure.Services;
using Azure.Storage.Blobs;
using Azure.AI.OpenAI;
using System.Net.Http;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Database
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();

        // Application Services
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<ILeadService, LeadService>();
        services.AddScoped<IBusinessIdentificationService, BusinessIdentificationService>();
        services.AddScoped<IBusinessConfigurationService, BusinessConfigurationService>();
        services.AddScoped<IConversationContextService, ConversationContextService>();
        services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();
        services.AddScoped<IWhatsAppWebhookParserService, WhatsAppWebhookParserService>();

        // Infrastructure Services - WhatsApp
        services.AddHttpClient();
        services.AddScoped<IWhatsAppService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            var config = sp.GetRequiredService<IConfiguration>();
            var phoneNumberId = config["WhatsApp:PhoneNumberId"] ?? throw new InvalidOperationException("WhatsApp:PhoneNumberId no configurado");
            var accessToken = config["WhatsApp:AccessToken"] ?? throw new InvalidOperationException("WhatsApp:AccessToken no configurado");
            var logger = sp.GetRequiredService<ILogger<WhatsAppService>>();
            
            return new WhatsAppService(httpClient, phoneNumberId, accessToken, logger);
        });

        // Infrastructure Services - OpenAI
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var endpoint = config["OpenAI:Endpoint"] ?? throw new InvalidOperationException("OpenAI:Endpoint no configurado");
            var apiKey = config["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey no configurado");
            
            return new OpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
        });

        services.AddScoped<IAIService>(sp =>
        {
            var client = sp.GetRequiredService<OpenAIClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var textDeploymentName = config["OpenAI:TextDeploymentName"] ?? "gpt-4o-mini";
            var audioDeploymentName = config["OpenAI:AudioDeploymentName"] ?? "whisper-1";
            var businessConfigService = sp.GetRequiredService<IBusinessConfigurationService>();
            var contextService = sp.GetRequiredService<IConversationContextService>();
            
            return new AIService(client, textDeploymentName, audioDeploymentName, businessConfigService, contextService, sp.GetRequiredService<ILogger<AIService>>());
        });

        // Infrastructure Services - Blob Storage
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config["BlobStorage:ConnectionString"] ?? throw new InvalidOperationException("BlobStorage:ConnectionString no configurado");
            
            return new BlobServiceClient(connectionString);
        });

        services.AddScoped<IBlobStorageService>(sp =>
        {
            var client = sp.GetRequiredService<BlobServiceClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var containerName = config["BlobStorage:ContainerName"] ?? "planes-images";
            
            return new BlobStorageService(client, containerName, sp.GetRequiredService<ILogger<BlobStorageService>>());
        });

        // Application Insights (opcional)
        // services.AddApplicationInsightsTelemetryWorkerService();
    })
    .Build();

host.Run();
