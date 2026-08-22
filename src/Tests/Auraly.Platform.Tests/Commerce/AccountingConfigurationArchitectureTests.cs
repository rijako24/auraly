using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class AccountingConfigurationArchitectureTests
{
    [Fact]
    public void Accounting_catalogs_and_source_equivalences_are_table_driven()
    {
        var root = FindSolutionRoot();
        var store = Read(root,
            "src/Modules/Accounting/Auraly.Commerce.Accounting.Infrastructure/SqlAccountingStore.cs");
        var processor = Read(root,
            "src/Modules/Accounting/Auraly.Commerce.Accounting.Infrastructure/SqlAccountingPostingProcessor.cs");
        var cashReasons = Read(root,
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlWorkSessionStore.CashReasons.cs");
        var page = Read(root,
            "admin/src/app/(dashboard)/dashboard/accounting/page.tsx");
        var schema = Read(root, "database/Auraly.Database/Tables/Accounting.sql");
        var provision = Read(root,
            "database/Auraly.Database/StoredProcedures/AccountingDefaultsProvision.sql");

        Regex.IsMatch(store, @"N?'[1-6]\d{5}'").Should().BeFalse(
            "PUC account codes belong to configuration tables, not runtime C#");
        store.Should().NotContain("DECLARE @Defaults");
        processor.Should().NotContain("=> AccountingCategories");
        processor.Should().NotContain("PaymentCategory(");
        cashReasons.Should().NotContain("DefaultCashReasons =");
        page.Should().NotContain("const categories");
        schema.Should().Contain("AccountingConfigurationProfileAccounts");
        schema.Should().Contain("AccountingSourceCategoryMappings");
        schema.Should().Contain("ReasonTemplates");
        schema.Should().Contain("BusinessReasons");
        schema.Should().Contain("AccountingConfigurationProfileExpenseConcepts");
        provision.Should().NotContain("DECLARE @Accounts");
        provision.Should().NotContain("DECLARE @ExpenseConcepts");
        provision.Should().Contain("AccountingConfigurationProfileAccounts");
        provision.Should().Contain("AccountingConfigurationProfileExpenseConcepts");
    }

    [Fact]
    public void Operational_reasons_use_the_shared_typed_catalog()
    {
        var root = FindSolutionRoot();
        var inventoryQuery = Read(root,
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlInventoryQueryStore.cs");
        var inventoryOperations = Read(root,
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlInventoryOperationStore.cs");
        var salesService = Read(root,
            "src/Modules/Returns/Auraly.Application.Returns/SalesReturnService.cs");
        var purchaseService = Read(root,
            "src/Modules/Purchasing/Auraly.Application.Purchasing/PurchaseReturnService.cs");
        var salesPage = Read(root,
            "admin/src/components/returns/sales-return-workspace.tsx");
        var purchasePage = Read(root,
            "admin/src/app/(dashboard)/dashboard/purchasing/purchase-returns/page.tsx");

        inventoryQuery.Should().NotContain("dbo.InventoryReasons");
        inventoryOperations.Should().NotContain("dbo.InventoryReasons");
        salesService.Should().NotContain("ReasonCodes.All");
        purchaseService.Should().NotContain("HashSet<string> Reasons");
        salesPage.Should().NotContain("const reasons =");
        purchasePage.Should().NotContain("const reasons =");
        inventoryQuery.Should().Contain("dbo.BusinessReasons");
        var dispatchStore = Read(root,
            "src/Modules/Dispatching/Auraly.Infrastructure.Dispatching/SqlDispatchDeliveryStore.cs");
        dispatchStore.Should().Contain("dbo.BusinessReasons");
        dispatchStore.Should().NotContain("dispatch.DispatchReasons");
    }

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindSolutionRoot()
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
