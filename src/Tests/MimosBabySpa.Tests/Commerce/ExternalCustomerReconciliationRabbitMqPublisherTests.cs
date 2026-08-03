using System.Text;
using Auraly.Contracts.Parties;
using MimosBabySpa.Infrastructure.Commerce;
using RabbitMQ.Client;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ExternalCustomerReconciliationRabbitMqPublisherTests
{
    [Fact]
    public async Task Real_publisher_emits_persistent_canonical_envelope()
    {
        var rabbitConnection = Environment.GetEnvironmentVariable("AURALY_TEST_RABBITMQ");
        if (string.IsNullOrWhiteSpace(rabbitConnection))
        {
            Assert.False(
                string.Equals(
                    Environment.GetEnvironmentVariable("AURALY_REQUIRE_RABBITMQ_TEST"),
                    "1",
                    StringComparison.Ordinal),
                "AURALY_TEST_RABBITMQ is required for the explicit RabbitMQ E2E run.");
            return;
        }

        var queue = $"auraly-tests-external-producer-{Guid.NewGuid():N}";
        var options = new ExternalCustomerReconciliationTransportOptions(
            "RabbitMq",
            rabbitConnection,
            queue);
        var signal = new ExternalCustomerReconciliationSignal(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        await using var publisher =
            new RabbitMqExternalCustomerReconciliationPublisher(options);

        await publisher.PublishAsync(signal);

        var factory = new ConnectionFactory
        {
            Uri = new Uri(rabbitConnection, UriKind.Absolute)
        };
        await using var connection = await factory.CreateConnectionAsync(
            "auraly-external-customer-publisher-test");
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            var delivery = await channel.BasicGetAsync(queue, autoAck: true);
            Assert.NotNull(delivery);
            Assert.True(delivery.BasicProperties.Persistent);
            Assert.Equal(signal.MessageId.ToString("D"), delivery.BasicProperties.MessageId);
            Assert.Equal("external-customer.reconcile", delivery.BasicProperties.Type);
            Assert.Equal(
                signal,
                ExternalCustomerReconciliationSignalCodec.Deserialize(
                    Encoding.UTF8.GetString(delivery.Body.Span)));
            Assert.Equal(
                signal.BusinessId.ToString("D"),
                Header(delivery.BasicProperties, "businessId"));
        }
        finally
        {
            await channel.QueueDeleteAsync(queue, false, false, false);
            await channel.QueueDeleteAsync($"{queue}.dead", false, false, false);
        }
    }

    private static string? Header(
        IReadOnlyBasicProperties properties,
        string name)
    {
        if (properties.Headers is null ||
            !properties.Headers.TryGetValue(name, out var value))
            return null;
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            _ => value?.ToString()
        };
    }
}
