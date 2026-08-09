using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.ClientModel;

namespace MimosBabySpa.Infrastructure.Configuration;

public static class AzureManagedClientFactory
{
    public static AzureOpenAIClient CreateAzureOpenAIClient(
        IConfiguration configuration,
        string endpoint,
        string? apiKey)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Configure a valid HTTPS Azure OpenAI endpoint.");
        }

        return string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(endpointUri, CreateCredential(
                configuration,
                "OpenAI:ManagedIdentityClientId"))
            : new AzureOpenAIClient(endpointUri, new ApiKeyCredential(apiKey));
    }

    public static ServiceBusClient CreateServiceBusClient(IConfiguration configuration)
    {
        var connectionString = configuration["ServiceBusConnection"]
            ?? configuration.GetConnectionString("ServiceBus");
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new ServiceBusClient(connectionString);

        var fullyQualifiedNamespace =
            configuration["ServiceBusConnection:fullyQualifiedNamespace"];
        if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        {
            throw new InvalidOperationException(
                "Configure ServiceBusConnection or ServiceBusConnection:fullyQualifiedNamespace.");
        }

        return new ServiceBusClient(
            fullyQualifiedNamespace,
            CreateCredential(configuration, "ServiceBusConnection:clientId"));
    }

    public static BlobServiceClient CreateBlobServiceClient(IConfiguration configuration)
    {
        var connectionString = configuration["AzureWebJobsStorage"]
            ?? configuration["AzureStorage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new BlobServiceClient(connectionString);

        var accountName = configuration["AzureWebJobsStorage:accountName"]
            ?? configuration["AzureStorage:AccountName"];
        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidOperationException(
                "Configure AzureWebJobsStorage or AzureWebJobsStorage:accountName.");
        }

        return new BlobServiceClient(
            new Uri($"https://{accountName}.blob.core.windows.net"),
            CreateCredential(configuration, "AzureWebJobsStorage:clientId"));
    }

    public static TokenCredential CreateCredential(
        IConfiguration configuration,
        string clientIdKey)
    {
        var clientId = configuration[clientIdKey]
            ?? configuration["Azure:ManagedIdentityClientId"]
            ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");

        return string.IsNullOrWhiteSpace(clientId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = clientId
            });
    }
}
