namespace Auraly.Foundation.Tests;

public sealed class ExternalCustomerReconciliationArchitectureTests
{
    [Fact]
    public void Canonical_consumer_is_durable_authorized_by_business_and_has_no_polling()
    {
        var root = FindRepositoryRoot();
        var contract = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "Parties",
            "Auraly.Contracts.Parties",
            "ExternalCustomerReconciliationSignal.cs"));
        var application = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "Parties",
            "Auraly.Application.Parties",
            "ExternalCustomerReconciliationSystemService.cs"));
        var consumer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "API",
            "Auraly.Api",
            "ExternalCustomerReconciliationHostedServices.cs"));
        var composition = File.ReadAllText(Path.Combine(
            root,
            "src",
            "API",
            "Auraly.Api",
            "Program.cs"));
        var outboxSchema = File.ReadAllText(Path.Combine(
            root,
            "database",
            "Auraly.Database",
            "Tables",
            "ExternalCustomerReconciliationOutboxMessages.sql"));
        var receiptSchema = File.ReadAllText(Path.Combine(
            root,
            "database",
            "Auraly.Database",
            "Tables",
            "ExternalCustomerReconciliationReceipts.sql"));

        Assert.Contains("ExternalCommerceCustomerId", contract);
        Assert.Contains("BusinessId", contract);
        Assert.Contains("ReceiptStatusAsync", application);
        Assert.Contains("ResolveIntegrationExecutionAsync", application);
        Assert.Contains("RecordReceiptAsync", application);
        Assert.Contains("CreateSessionProcessor", consumer);
        Assert.Contains("MaxConcurrentCallsPerSession = 1", consumer);
        Assert.Contains("BasicQosAsync(0, 1, false", consumer);
        Assert.Contains("BasicAckAsync", consumer);
        Assert.Contains("BasicNackAsync", consumer);
        Assert.DoesNotContain("PeriodicTimer", consumer, StringComparison.Ordinal);
        Assert.Contains(
            "ExternalCustomerReconciliationRabbitMqHostedService",
            composition,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExternalCustomerReconciliationServiceBusHostedService",
            composition,
            StringComparison.Ordinal);
        Assert.Contains(
            "UX_ExternalCustomerReconciliationOutboxMessages_PendingCustomer",
            outboxSchema,
            StringComparison.Ordinal);
        Assert.Contains(
            "PRIMARY KEY CLUSTERED ([MessageId])",
            receiptSchema,
            StringComparison.Ordinal);
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
}
