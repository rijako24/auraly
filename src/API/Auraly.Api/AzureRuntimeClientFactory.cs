using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.WebPubSub;

namespace Auraly.Api;

internal static class AzureRuntimeClientFactory
{
    public static ServiceBusClient CreateServiceBusClient(IConfiguration configuration)
    {
        var connectionString = configuration["ServiceBusConnection"]
            ?? configuration.GetConnectionString("ServiceBus");
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new ServiceBusClient(connectionString);

        var fullyQualifiedNamespace =
            configuration["ServiceBusConnection:fullyQualifiedNamespace"];
        if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
            throw new InvalidOperationException(
                "Configure ServiceBusConnection or ServiceBusConnection:fullyQualifiedNamespace.");

        return new ServiceBusClient(
            fullyQualifiedNamespace,
            CreateCredential(configuration, "ServiceBusConnection:clientId"));
    }

    public static WebPubSubServiceClient CreateWebPubSubServiceClient(
        IConfiguration configuration,
        string? connectionString,
        string? endpoint,
        string hub)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new WebPubSubServiceClient(connectionString, hub);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "Configure a valid HTTPS Auraly:PosSynchronization:WebPubSub:Endpoint.");

        return new WebPubSubServiceClient(
            endpointUri,
            hub,
            CreateCredential(
                configuration,
                "Auraly:PosSynchronization:WebPubSub:ManagedIdentityClientId"));
    }

    private static TokenCredential CreateCredential(
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
