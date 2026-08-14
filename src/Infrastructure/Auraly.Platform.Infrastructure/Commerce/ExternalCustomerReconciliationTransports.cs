using System.Text;
using Auraly.Contracts.Parties;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Infrastructure.Configuration;
using RabbitMQ.Client;

namespace Auraly.Platform.Infrastructure.Commerce;

public sealed record ExternalCustomerReconciliationTransportOptions(
    string Transport,
    string ConnectionString,
    string QueueName);

public static class ExternalCustomerReconciliationMessagingRegistration
{
    public static IServiceCollection AddExternalCustomerReconciliationMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var transport = configuration[
                "Auraly:ExternalCustomerReconciliation:Transport"]
            ?? configuration["Auraly:Processing:Transport"]
            ?? "ServiceBus";
        var queueName = configuration[
                "Auraly:ExternalCustomerReconciliation:QueueName"]
            ?? "auraly-external-customer-reconciliation";
        string? connectionString;
        if (string.Equals(transport, "ServiceBus", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = configuration[
                    "Auraly:ExternalCustomerReconciliation:ServiceBus:ConnectionString"]
                ?? configuration["ServiceBusConnection"]
                ?? configuration.GetConnectionString("ServiceBus");
            var fullyQualifiedNamespace =
                configuration["ServiceBusConnection:fullyQualifiedNamespace"];
            if (string.IsNullOrWhiteSpace(connectionString) &&
                string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
            {
                throw new InvalidOperationException(
                    "External-customer reconciliation ServiceBus connection is required; configure a connection string or fully qualified namespace. Committed events never fall back to polling.");
            }
        }
        else if (string.Equals(transport, "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = configuration[
                    "Auraly:ExternalCustomerReconciliation:RabbitMq:ConnectionString"]
                ?? configuration["Auraly:Processing:RabbitMq:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "External-customer reconciliation RabbitMq connection is required; committed events never fall back to polling.");
            }
        }
        else
            throw new InvalidOperationException(
                "External-customer reconciliation transport must be ServiceBus or RabbitMq.");

        var options = new ExternalCustomerReconciliationTransportOptions(
            transport,
            connectionString ?? string.Empty,
            queueName);
        services.AddSingleton(options);
        services.TryAddSingleton<IConfiguration>(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ExternalCustomerReconciliationOutboxSignal>();
        services.AddScoped<ExternalCustomerReconciliationCommitState>();
        services.AddScoped<SqlExternalCustomerReconciliationOutboxDispatcher>();
        services.AddHostedService<ExternalCustomerReconciliationOutboxHostedService>();
        if (string.Equals(transport, "ServiceBus", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IExternalCustomerReconciliationSignalPublisher,
                ServiceBusExternalCustomerReconciliationPublisher>();
        else
            services.AddSingleton<IExternalCustomerReconciliationSignalPublisher,
                RabbitMqExternalCustomerReconciliationPublisher>();
        return services;
    }
}

public sealed class ServiceBusExternalCustomerReconciliationPublisher
    : IExternalCustomerReconciliationSignalPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient client;
    private readonly ServiceBusSender sender;

    public ServiceBusExternalCustomerReconciliationPublisher(
        ExternalCustomerReconciliationTransportOptions options,
        IConfiguration configuration)
    {
        client = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? AzureManagedClientFactory.CreateServiceBusClient(configuration)
            : new ServiceBusClient(options.ConnectionString);
        sender = client.CreateSender(options.QueueName);
    }

    public async Task PublishAsync(
        ExternalCustomerReconciliationSignal signal,
        CancellationToken cancellationToken = default)
    {
        var message = new ServiceBusMessage(BinaryData.FromString(
            ExternalCustomerReconciliationSignalCodec.Serialize(signal)))
        {
            MessageId = signal.MessageId.ToString("D"),
            SessionId = signal.BusinessId.ToString("D"),
            Subject = "external-customer.reconcile",
            ContentType = "application/json"
        };
        message.ApplicationProperties["externalCommerceCustomerId"] =
            signal.ExternalCommerceCustomerId.ToString("D");
        await sender.SendMessageAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await sender.DisposeAsync();
        await client.DisposeAsync();
    }
}

public sealed class RabbitMqExternalCustomerReconciliationPublisher
    : IExternalCustomerReconciliationSignalPublisher, IAsyncDisposable
{
    private readonly ExternalCustomerReconciliationTransportOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public RabbitMqExternalCustomerReconciliationPublisher(
        ExternalCustomerReconciliationTransportOptions options) =>
        this.options = options;

    public async Task PublishAsync(
        ExternalCustomerReconciliationSignal signal,
        CancellationToken cancellationToken = default)
    {
        ExternalCustomerReconciliationSignalCodec.Validate(signal);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var active = await GetChannelAsync(cancellationToken);
            await EnsureTopologyAsync(active, options.QueueName, cancellationToken);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = signal.MessageId.ToString("D"),
                Type = "external-customer.reconcile",
                Headers = new Dictionary<string, object?>
                {
                    ["businessId"] = signal.BusinessId.ToString("D"),
                    ["externalCommerceCustomerId"] =
                        signal.ExternalCommerceCustomerId.ToString("D")
                }
            };
            await active.BasicPublishAsync(
                string.Empty,
                options.QueueName,
                true,
                properties,
                Encoding.UTF8.GetBytes(
                    ExternalCustomerReconciliationSignalCodec.Serialize(signal)),
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    internal static async Task EnsureTopologyAsync(
        IChannel channel,
        string queueName,
        CancellationToken cancellationToken)
    {
        var deadQueue = $"{queueName}.dead";
        await channel.QueueDeclareAsync(
            deadQueue, true, false, false, null, false, false, cancellationToken);
        await channel.QueueDeclareAsync(
            queueName,
            true,
            false,
            false,
            new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = deadQueue
            },
            false,
            false,
            cancellationToken);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true }) return channel;
        if (channel is not null) await channel.DisposeAsync();
        if (connection is not { IsOpen: true })
        {
            if (connection is not null) await connection.DisposeAsync();
            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.ConnectionString, UriKind.Absolute),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };
            connection = await factory.CreateConnectionAsync(
                "auraly-external-customer-producer",
                cancellationToken);
        }
        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, true),
            cancellationToken);
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (channel is not null) await channel.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        gate.Dispose();
    }
}
