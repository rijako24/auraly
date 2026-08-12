using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Infrastructure.Commerce;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ExternalCustomerReconciliationMessagingRegistrationTests
{
    [Fact]
    public async Task ServiceBus_registration_accepts_managed_identity_configuration()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ServiceBusConnection:fullyQualifiedNamespace"] =
                "sb-auraly-dev.servicebus.windows.net",
            ["ServiceBusConnection:clientId"] = Guid.NewGuid().ToString("D")
        });
        var services = new ServiceCollection();

        services.AddExternalCustomerReconciliationMessaging(configuration);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<
            ExternalCustomerReconciliationTransportOptions>();
        Assert.Equal("ServiceBus", options.Transport);
        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.Equal("auraly-external-customer-reconciliation", options.QueueName);
        Assert.IsType<ServiceBusExternalCustomerReconciliationPublisher>(
            provider.GetRequiredService<
                IExternalCustomerReconciliationSignalPublisher>());
    }

    [Fact]
    public void ServiceBus_registration_rejects_missing_connection_configuration()
    {
        var configuration = Configuration([]);
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddExternalCustomerReconciliationMessaging(configuration));

        Assert.Contains("fully qualified namespace", error.Message);
        Assert.Contains("never fall back to polling", error.Message);
    }

    [Fact]
    public void RabbitMq_registration_still_requires_its_connection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Auraly:ExternalCustomerReconciliation:Transport"] = "RabbitMq"
        });
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddExternalCustomerReconciliationMessaging(configuration));

        Assert.Contains("RabbitMq connection is required", error.Message);
    }

    private static IConfiguration Configuration(
        IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
