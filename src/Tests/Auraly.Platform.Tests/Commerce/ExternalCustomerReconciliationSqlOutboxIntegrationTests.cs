using System.Diagnostics;
using System.Text;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Infrastructure.Commerce;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Repositories;
using RabbitMQ.Client;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class ExternalCustomerReconciliationSqlOutboxIntegrationTests
{
    [Fact]
    public async Task Real_sql_outbox_publishes_once_and_preserves_failed_message()
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

        var sqlServer = Environment.GetEnvironmentVariable("AURALY_TEST_SQLSERVER")
            ?? @".\LOCAL";
        var database = $"AuralyExternalOutbox_{Guid.NewGuid():N}";
        var connectionString =
            $"Server={sqlServer};Initial Catalog={database};Integrated Security=True;TrustServerCertificate=True;";
        var queue = $"auraly-tests-external-outbox-{Guid.NewGuid():N}";
        await DeployDacpacAsync(connectionString);
        try
        {
            var seeded = await SeedAsync(connectionString);
            var produced = await ProduceAsync(connectionString, seeded, "3005550971");
            var options = new ExternalCustomerReconciliationTransportOptions(
                "RabbitMq",
                rabbitConnection,
                queue);
            await using var publisher =
                new RabbitMqExternalCustomerReconciliationPublisher(options);
            await using (var context = Context(connectionString))
            {
                var dispatcher = new SqlExternalCustomerReconciliationOutboxDispatcher(
                    context,
                    publisher,
                    TimeProvider.System,
                    NullLogger<SqlExternalCustomerReconciliationOutboxDispatcher>.Instance);
                var outcome = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
                Assert.Equal(1, outcome.Published);
                Assert.Equal(0, outcome.Failed);
            }

            var received = await ReadRabbitAsync(rabbitConnection, queue);
            Assert.Equal(produced.MessageId.ToString("D"), received.MessageId);
            Assert.Equal(produced, received.Signal);
            await using (var sql = new SqlConnection(connectionString))
            {
                await sql.OpenAsync();
                await using var command = sql.CreateCommand();
                command.CommandText = """
                    SELECT COUNT(*) FROM dbo.ExternalCustomerReconciliationOutboxMessages
                    WHERE MessageId=@MessageId AND PublishedAt IS NOT NULL
                      AND AttemptCount=1 AND LastError IS NULL;
                    """;
                command.Parameters.AddWithValue("@MessageId", produced.MessageId);
                Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
            }

            var failed = await ProduceAsync(connectionString, seeded, "3005550972");
            await using (var context = Context(connectionString))
            {
                var dispatcher = new SqlExternalCustomerReconciliationOutboxDispatcher(
                    context,
                    new AlwaysFailPublisher(),
                    TimeProvider.System,
                    NullLogger<SqlExternalCustomerReconciliationOutboxDispatcher>.Instance);
                var outcome = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
                Assert.Equal(0, outcome.Published);
                Assert.Equal(1, outcome.Failed);
                Assert.NotNull(outcome.NextAttemptAt);
            }
            await using (var sql = new SqlConnection(connectionString))
            {
                await sql.OpenAsync();
                await using var command = sql.CreateCommand();
                command.CommandText = """
                    SELECT COUNT(*) FROM dbo.ExternalCustomerReconciliationOutboxMessages
                    WHERE MessageId=@MessageId AND PublishedAt IS NULL
                      AND AttemptCount=1 AND LastError LIKE N'%simulated broker failure%';
                    """;
                command.Parameters.AddWithValue("@MessageId", failed.MessageId);
                Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await DeleteQueuesAsync(rabbitConnection, queue);
            await DropDatabaseAsync(sqlServer, database);
        }
    }

    private static async Task<Seed> SeedAsync(string connectionString)
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.Tenants(TenantId,Name,Email,IsActive,CreatedAt)
            VALUES(@TenantId,N'External bridge test',@Email,1,SYSUTCDATETIME());
            INSERT dbo.Businesses
              (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,
               TimeZone,IsActive,CreatedAt)
            VALUES
              (@BusinessId,@TenantId,N'External bridge test',N'',N'',N'',@BusinessEmail,
               N'',N'America/Bogota',1,SYSUTCDATETIME());
            INSERT dbo.IntegrationConnections
              (IntegrationConnectionId,BusinessId,ConnectionType,Provider,Capability,
               Name,SettingsJson,IsEnabled,CreatedAt)
            VALUES(@IntegrationId,@BusinessId,0,98101,98102,N'External bridge',
                   N'{}',1,SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@TenantId", seed.TenantId);
        command.Parameters.AddWithValue("@BusinessId", seed.BusinessId);
        command.Parameters.AddWithValue("@IntegrationId", seed.IntegrationId);
        command.Parameters.AddWithValue("@Email", $"{seed.TenantId:N}@test.auraly");
        command.Parameters.AddWithValue("@BusinessEmail", $"{seed.BusinessId:N}@test.auraly");
        await command.ExecuteNonQueryAsync();
        return seed;
    }

    private static async Task<ExternalCustomerReconciliationSignal> ProduceAsync(
        string connectionString,
        Seed seed,
        string phone)
    {
        await using var context = Context(connectionString);
        var state = new ExternalCustomerReconciliationCommitState();
        var repository = new ExternalCommerceCustomerRepository(
            context,
            state,
            new Uuid7AuralyIdGenerator(TimeProvider.System));
        var id = Guid.NewGuid();
        await repository.CreateAsync(new ExternalCommerceCustomer
        {
            ExternalCommerceCustomerId = id,
            BusinessId = seed.BusinessId,
            IntegrationConnectionId = seed.IntegrationId,
            ExternalAccountId = $"account-{id:N}",
            ExternalCustomerId = $"customer-{id:N}",
            Name = "Cliente outbox",
            PhoneNormalized = phone,
            Phone = phone,
            IsActive = true,
            LastSyncedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var message = await context.ExternalCustomerReconciliationOutboxMessages
            .SingleAsync(row => row.ExternalCommerceCustomerId == id);
        return new ExternalCustomerReconciliationSignal(
            message.MessageId,
            message.ExternalCommerceCustomerId,
            message.BusinessId,
            message.OccurredAt);
    }

    private static ApplicationDbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<RabbitEvidence> ReadRabbitAsync(
        string connectionString,
        string queue)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString, UriKind.Absolute)
        };
        await using var connection = await factory.CreateConnectionAsync(
            "auraly-external-outbox-test");
        await using var channel = await connection.CreateChannelAsync();
        var delivery = await channel.BasicGetAsync(queue, autoAck: true)
            ?? throw new InvalidOperationException("The outbox message did not reach RabbitMQ.");
        return new RabbitEvidence(
            delivery.BasicProperties.MessageId,
            ExternalCustomerReconciliationSignalCodec.Deserialize(
                Encoding.UTF8.GetString(delivery.Body.Span)));
    }

    private static async Task DeleteQueuesAsync(
        string connectionString,
        string queue)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString, UriKind.Absolute)
        };
        await using var connection = await factory.CreateConnectionAsync(
            "auraly-external-outbox-cleanup");
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeleteAsync(queue, false, false, false);
        await channel.QueueDeleteAsync($"{queue}.dead", false, false, false);
    }

    private static async Task DeployDacpacAsync(string connectionString)
    {
        var root = FindRepositoryRoot();
        var dacpac = Path.Combine(
            root,
            "database",
            "Auraly.Database",
            "bin",
            "Release",
            "Auraly.Database.dacpac");
        if (!File.Exists(dacpac))
            throw new FileNotFoundException(
                "Build Auraly.Database in Release before running this test.",
                dacpac);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(FindSqlPackage())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("/Action:Publish");
        process.StartInfo.ArgumentList.Add($"/SourceFile:{dacpac}");
        process.StartInfo.ArgumentList.Add($"/TargetConnectionString:{connectionString}");
        process.StartInfo.ArgumentList.Add("/p:CreateNewDatabase=True");
        process.StartInfo.ArgumentList.Add("/p:DropObjectsNotInSource=False");
        process.StartInfo.ArgumentList.Add("/p:BlockOnPossibleDataLoss=True");
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"SqlPackage failed.{Environment.NewLine}{await output}{Environment.NewLine}{await error}");
    }

    private static async Task DropDatabaseAsync(string server, string database)
    {
        var master =
            $"Server={server};Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True;";
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{database}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static string FindSqlPackage()
    {
        var configured = Environment.GetEnvironmentVariable("SQLPACKAGE_PATH");
        var candidates = new[]
        {
            configured,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet",
                "tools",
                "sqlpackage.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft SQL Server",
                "160",
                "DAC",
                "bin",
                "SqlPackage.exe")
        };
        return candidates.FirstOrDefault(path =>
                   !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? throw new FileNotFoundException(
                "SqlPackage was not found. Set SQLPACKAGE_PATH.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Auraly.Commerce.sln.");
    }

    private sealed record Seed(Guid TenantId, Guid BusinessId, Guid IntegrationId);
    private sealed record RabbitEvidence(
        string? MessageId,
        ExternalCustomerReconciliationSignal Signal);

    private sealed class AlwaysFailPublisher
        : IExternalCustomerReconciliationSignalPublisher
    {
        public Task PublishAsync(
            ExternalCustomerReconciliationSignal signal,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated broker failure");
    }
}
