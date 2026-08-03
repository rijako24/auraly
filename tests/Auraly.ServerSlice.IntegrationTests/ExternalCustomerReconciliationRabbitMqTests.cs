using System.Text;
using Auraly.Api;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ExternalCustomerReconciliationRabbitMqTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Real_broker_delivers_once_replays_safely_and_dead_letters_poison_message()
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

        var suffix = Guid.NewGuid().ToString("N");
        var queue = $"auraly-tests-external-customers-{suffix}";
        var options = new RabbitMqProcessingOptions(
            rabbitConnection,
            $"unused-documents-{suffix}",
            $"unused-fiscal-{suffix}");
        await using var connection = new RabbitMqProcessingConnection(options);
        using var service = new ExternalCustomerReconciliationRabbitMqHostedService(
            connection,
            new ExternalCustomerReconciliationRabbitMqOptions(queue),
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ExternalCustomerReconciliationRabbitMqHostedService>.Instance);
        var integrationId = await CreateIntegrationAsync();
        var externalId = await CreateExternalAsync(integrationId);
        var signal = new ExternalCustomerReconciliationSignal(
            Guid.NewGuid(),
            externalId,
            fixture.BusinessId,
            DateTimeOffset.UtcNow);

        await DeclareAsync(connection, queue);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await PublishAsync(connection, queue, signal);
            await WaitUntilAsync(async () =>
                await ScalarAsync<int>("""
                    SELECT COUNT(*) FROM dbo.ExternalCustomerReconciliationReceipts
                    WHERE MessageId=@MessageId;
                    """, new SqlParameter("@MessageId", signal.MessageId)) == 1);

            Assert.Equal("Linked", await ScalarAsync<string>("""
                SELECT ReconciliationStatus FROM dbo.ExternalCommerceCustomers
                WHERE ExternalCommerceCustomerId=@ExternalId;
                """, new SqlParameter("@ExternalId", externalId)));
            var partyId = await ScalarAsync<Guid>("""
                SELECT PartyId FROM dbo.ExternalCommerceCustomers
                WHERE ExternalCommerceCustomerId=@ExternalId;
                """, new SqlParameter("@ExternalId", externalId));
            var customerId = await ScalarAsync<Guid>("""
                SELECT CustomerId FROM dbo.ExternalCommerceCustomers
                WHERE ExternalCommerceCustomerId=@ExternalId;
                """, new SqlParameter("@ExternalId", externalId));

            await PublishAsync(connection, queue, signal);
            await WaitUntilAsync(async () => await QueueCountAsync(connection, queue) == 0);
            Assert.Equal(1, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Parties WHERE PartyId=@PartyId;",
                new SqlParameter("@PartyId", partyId)));
            Assert.Equal(1, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Customers WHERE CustomerId=@CustomerId;",
                new SqlParameter("@CustomerId", customerId)));
            Assert.Equal(1, await ScalarAsync<int>("""
                SELECT COUNT(*) FROM dbo.ExternalCustomerReconciliationReceipts
                WHERE MessageId=@MessageId;
                """, new SqlParameter("@MessageId", signal.MessageId)));

            var poison = new ExternalCustomerReconciliationSignal(
                Guid.NewGuid(),
                Guid.NewGuid(),
                fixture.BusinessId,
                DateTimeOffset.UtcNow);
            await PublishAsync(connection, queue, poison);
            await WaitUntilAsync(
                async () => await QueueCountAsync(connection, $"{queue}.dead") == 1,
                TimeSpan.FromSeconds(20));
            Assert.Equal(0, await ScalarAsync<int>("""
                SELECT COUNT(*) FROM dbo.ExternalCustomerReconciliationReceipts
                WHERE MessageId=@MessageId;
                """, new SqlParameter("@MessageId", poison.MessageId)));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await DeleteAsync(connection, queue);
        }
    }

    private async Task<Guid> CreateIntegrationAsync()
    {
        var id = Guid.NewGuid();
        var discriminator = id.GetHashCode() & int.MaxValue;
        await ExecuteAsync("""
            INSERT dbo.IntegrationConnections
              (IntegrationConnectionId,BusinessId,ConnectionType,Provider,Capability,Name,
               SettingsJson,IsEnabled,CreatedAt)
            VALUES(@Id,@BusinessId,0,@Provider,@Capability,N'Rabbit customer source',
                   N'{}',1,SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@BusinessId", fixture.BusinessId),
            new SqlParameter("@Provider", discriminator),
            new SqlParameter("@Capability", discriminator));
        return id;
    }

    private async Task<Guid> CreateExternalAsync(Guid integrationId)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.ExternalCommerceCustomers
              (ExternalCommerceCustomerId,BusinessId,IntegrationConnectionId,ExternalAccountId,
               ExternalCustomerId,Name,PhoneNormalized,Phone,IsActive,LastSyncedAt,CreatedAt)
            VALUES
              (@Id,@BusinessId,@IntegrationId,@AccountId,@CustomerId,N'Cliente Rabbit',
               N'3005550991',N'3005550991',1,SYSUTCDATETIME(),SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@BusinessId", fixture.BusinessId),
            new SqlParameter("@IntegrationId", integrationId),
            new SqlParameter("@AccountId", $"account-{id:N}"),
            new SqlParameter("@CustomerId", $"customer-{id:N}"));
        return id;
    }

    private static async Task DeclareAsync(
        RabbitMqProcessingConnection connection,
        string queue)
    {
        await using var channel = await connection.CreateChannelAsync(false, default);
        await channel.QueueDeclareAsync(
            $"{queue}.dead", true, false, false, null, false, false, default);
        await channel.QueueDeclareAsync(
            queue,
            true,
            false,
            false,
            new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = $"{queue}.dead"
            },
            false,
            false,
            default);
    }

    private static async Task PublishAsync(
        RabbitMqProcessingConnection connection,
        string queue,
        ExternalCustomerReconciliationSignal signal)
    {
        await using var channel = await connection.CreateChannelAsync(true, default);
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
        await channel.BasicPublishAsync(
            string.Empty,
            queue,
            true,
            properties,
            Encoding.UTF8.GetBytes(
                ExternalCustomerReconciliationSignalCodec.Serialize(signal)),
            default);
    }

    private static async Task<uint> QueueCountAsync(
        RabbitMqProcessingConnection connection,
        string queue)
    {
        await using var channel = await connection.CreateChannelAsync(false, default);
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    private static async Task DeleteAsync(
        RabbitMqProcessingConnection connection,
        string queue)
    {
        await using var channel = await connection.CreateChannelAsync(false, default);
        await channel.QueueDeleteAsync(queue, false, false, false);
        await channel.QueueDeleteAsync($"{queue}.dead", false, false, false);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(
            timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition())
            await Task.Delay(100, cancellation.Token);
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        return value is T typed ? typed : (T)Convert.ChangeType(value!, typeof(T));
    }
}
