namespace Auraly.Foundation.Tests;

public sealed class ExternalCustomerReconciliationArchitectureTests
{
    [Fact]
    public void Reconciliation_is_explicit_and_has_no_queue_or_background_worker()
    {
        var root = FindRepositoryRoot();
        var application = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "Parties",
            "Auraly.Application.Parties",
            "ExternalCustomerReconciliationService.cs"));
        var composition = File.ReadAllText(Path.Combine(
            root,
            "src",
            "API",
            "Auraly.Api",
            "Program.cs"));
        var customerLookup = File.ReadAllText(Path.Combine(
            root,
            "src", "Infrastructure", "Auraly.Platform.Infrastructure", "Commerce",
            "CanonicalCommerceCustomerLookup.cs"));

        Assert.Contains("ReconcilePendingAsync", application);
        Assert.Contains("dbo.Customers", customerLookup);
        Assert.Contains("dbo.Parties", customerLookup);
        Assert.Contains("dbo.PartyContacts", customerLookup);
        Assert.DoesNotContain("ExternalCustomerReconciliationHostedService", composition,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalCustomerReconciliation:QueueName", composition,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "src", "API", "Auraly.Api",
            "ExternalCustomerReconciliationHostedServices.cs")));
        Assert.False(File.Exists(Path.Combine(root, "database", "Auraly.Database", "Tables",
            "ExternalCustomerReconciliationOutboxMessages.sql")));
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
