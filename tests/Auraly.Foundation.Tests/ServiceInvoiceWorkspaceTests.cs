using Auraly.Application.Sales;
using Auraly.Contracts.Sales;

namespace Auraly.Foundation.Tests;

public sealed class ServiceInvoiceWorkspaceTests
{
    [Fact]
    public async Task Issue_demands_price_override_and_discount_permissions_server_side()
    {
        var service = new ServiceInvoiceWorkspaceService(new CapturingStore());
        var identity = new ServiceInvoiceUserIdentity(
            Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string>(StringComparer.Ordinal)
            {
                ServiceInvoicePermissionCodes.Create,
                ServiceInvoicePermissionCodes.Issue
            });
        var request = Request(unitPrice: 12_000m, discount: 0);

        var exception = await Assert.ThrowsAsync<ServiceInvoiceForbiddenException>(
            () => service.IssueAsync(identity, request, "issue-1"));

        Assert.Contains(ServiceInvoicePermissionCodes.OverridePrice, exception.Message);
    }

    [Fact]
    public async Task Issue_rejects_incoherent_credit_before_reaching_persistence()
    {
        var store = new CapturingStore();
        var service = new ServiceInvoiceWorkspaceService(store);
        var identity = IdentityWithAllPermissions();
        var request = Request() with { CreditAmount = 1000, CreditDueDate = null };

        await Assert.ThrowsAsync<ServiceInvoiceValidationException>(
            () => service.IssueAsync(identity, request, "issue-2"));

        Assert.False(store.WasCalled);
    }

    [Fact]
    public void Canonical_writer_has_no_inventory_or_operational_job_effects()
    {
        var root = FindRepositoryRoot();
        var writer = File.ReadAllText(Path.Combine(root, "src", "Infrastructure",
            "Auraly.Infrastructure.Persistence", "SqlServiceInvoiceDocumentWriter.cs"));
        var store = File.ReadAllText(Path.Combine(root, "src", "Infrastructure",
            "Auraly.Infrastructure.Persistence", "SqlServiceInvoiceStore.cs"));

        Assert.Contains("INSERT dbo.SalesDocuments", writer, StringComparison.Ordinal);
        Assert.Contains("INSERT sales.SalesDocumentServiceLines", writer, StringComparison.Ordinal);
        Assert.Contains("INSERT dbo.AccountingPostingJobs", writer, StringComparison.Ordinal);
        Assert.Contains("INSERT reporting.SalesReportingJobs", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentProcessingJobs", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryMovements", writer, StringComparison.Ordinal);
        Assert.True(
            store.IndexOf("SqlDianDocumentQuota.TryReserveAsync", StringComparison.Ordinal) <
            store.IndexOf("ConsumeDocumentNumberAsync", StringComparison.Ordinal));
    }

    private static IssueServiceInvoiceRequest Request(
        decimal? unitPrice = null,
        decimal discount = 0) =>
        new(Guid.NewGuid(), Guid.NewGuid(),
            [new(Guid.NewGuid(), 1, UnitPrice: unitPrice,
                DiscountKind: discount > 0 ? "Value" : null,
                DiscountValue: discount)],
            "Transfer");

    private static ServiceInvoiceUserIdentity IdentityWithAllPermissions() =>
        new(Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string>(StringComparer.Ordinal)
            {
                ServiceInvoicePermissionCodes.Create,
                ServiceInvoicePermissionCodes.Issue,
                ServiceInvoicePermissionCodes.OverridePrice,
                ServiceInvoicePermissionCodes.Discount
            });

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Auraly.Commerce.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class CapturingStore : IServiceInvoiceStore
    {
        public bool WasCalled { get; private set; }

        public Task<BillableServicePage> SearchServicesAsync(
            ServiceInvoiceUserIdentity user,
            ServiceInvoiceSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BillableServicePage([], 1, 20, 0));

        public Task<ServiceInvoiceCustomerPage> SearchCustomersAsync(
            ServiceInvoiceUserIdentity user,
            ServiceInvoiceSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInvoiceCustomerPage([], 1, 20, 0));

        public Task<ServiceInvoiceHistoryPage> SearchInvoicesAsync(
            ServiceInvoiceUserIdentity user,
            ServiceInvoiceHistoryRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInvoiceHistoryPage([], request.Page, request.PageSize, 0));

        public Task<ServiceInvoiceDetail?> GetInvoiceAsync(
            ServiceInvoiceUserIdentity user,
            Guid businessId,
            Guid documentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ServiceInvoiceDetail?>(null);

        public Task<IssuedServiceInvoice> IssueAsync(
            ServiceInvoiceUserIdentity user,
            IssueServiceInvoiceRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new IssuedServiceInvoice(
                Guid.NewGuid(), "FSV-1", "SETP1", "cufe", 100, 19, 119, 0,
                "PendingGeneration", false));
        }
    }
}
