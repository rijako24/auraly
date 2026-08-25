using System.Text.RegularExpressions;

namespace Auraly.Foundation.Tests;

public sealed class ArchitectureDebtRatchetTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void InventoryLedger_HasOneWriter()
    {
        var files = CSharpFiles("src");
        var writerPattern = new Regex(
            @"\b(?:INSERT(?:\s+INTO)?|UPDATE)\s+dbo\.(?:InventoryBalances|InventoryMovements)\b",
            RegexOptions.IgnoreCase);
        var writers = files.Where(file => writerPattern.IsMatch(File.ReadAllText(file)));

        var writer = Assert.Single(writers);
        Assert.Equal(
            Path.Combine(RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence", "SqlInventoryLedgerWriter.cs"),
            writer);
    }

    [Fact]
    public void CanonicalEngines_AreNotDuplicated()
    {
        AssertSingleClass("DocumentProcessingEngine");
        AssertSingleClass("FiscalProcessingCoordinator");
        AssertSingleClass("AccountingProcessingCoordinator");
    }

    [Fact]
    public void ConfirmedDocumentHandlers_ShareTheEngineTransaction()
    {
        var handlers = CSharpFiles(Path.Combine("src", "Infrastructure", "Auraly.Infrastructure.Persistence"))
            .Where(file => File.ReadAllText(file).Contains("IConfirmedDocumentHandler", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(handlers);
        foreach (var handler in handlers)
        {
            var source = File.ReadAllText(handler);
            Assert.True(
                source.Contains("sessions.Current", StringComparison.Ordinal) ||
                source.Contains("_sessions.Current", StringComparison.Ordinal),
                $"{Path.GetFileName(handler)} must use the document engine SQL session.");
            Assert.DoesNotMatch(@"\bBeginTransaction(?:Async)?\s*\(", source);
            Assert.DoesNotMatch(@"\.Commit(?:Async)?\s*\(", source);
            Assert.DoesNotMatch(@"\bSaveChanges(?:Async)?\s*\(", source);
        }
    }

    [Fact]
    public void AccountingAndReportingProcessors_OwnOneSerializableSqlTransaction()
    {
        var paths = new[]
        {
            Path.Combine("src", "Modules", "Accounting",
                "Auraly.Commerce.Accounting.Infrastructure", "SqlAccountingPostingProcessor.cs"),
            Path.Combine("src", "Infrastructure", "Auraly.Infrastructure.Persistence",
                "SqlSalesReportingProcessor.cs")
        };

        foreach (var path in paths)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot, path));
            Assert.Single(Regex.Matches(source, @"BeginTransactionAsync\s*\("));
            Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
            Assert.Contains("CommitAsync", source, StringComparison.Ordinal);
            Assert.Contains("RollbackAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SaveChanges", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DocumentProcessingJobStore_OwnsCommitAndRollback()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Infrastructure",
            "Auraly.Infrastructure.Persistence",
            "SqlDocumentProcessingJobStore.cs"));

        Assert.Contains("session.Transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("session.Transaction.RollbackAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiSqlDebt_CannotGrow()
    {
        var count = CSharpFiles(Path.Combine("src", "API", "Auraly.Api"))
            .Sum(file => Regex.Matches(File.ReadAllText(file), @"(?:new\s+)?SqlCommand\s*\(").Count);

        Assert.True(count <= 15,
            $"Direct SQL in the API grew to {count}. Use EF Core or a versioned stored procedure; the reduced DT-003 baseline is 15.");
    }

    [Fact]
    public void SellerOrderApi_UsesStoredProceduresInsteadOfEmbeddedSql()
    {
        var paths = new[]
        {
            Path.Combine("src", "API", "Auraly.Api", "SellerOrdersApi.cs"),
            Path.Combine("src", "Infrastructure", "Auraly.Infrastructure.Persistence", "SellerOrderReviewPersistence.cs")
        };
        foreach (var path in paths)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot, path));
            Assert.DoesNotMatch(@"\b(?:SELECT|INSERT|UPDATE|DELETE)\s+(?:INTO\s+)?dbo\.", source);
            Assert.DoesNotContain("CommandText =", source, StringComparison.Ordinal);
            Assert.Contains("CommandType", source, StringComparison.Ordinal);
            Assert.Contains("CommandType.StoredProcedure", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SellerOrderAndInventoryProcedures_ParticipateInTheCallerTransaction()
    {
        var sellerSource = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "SellerOrdersApi.cs"));
        Assert.Contains("Procedure(\"dbo.SellerOrderCreate\",connection,transaction)", sellerSource, StringComparison.Ordinal);
        Assert.Contains("ConfirmTransferAtomicallyAsync", sellerSource, StringComparison.Ordinal);
        Assert.Contains("Procedure(\"dbo.SellerOrderConfirm\",connection,transaction)", sellerSource, StringComparison.Ordinal);

        var procedures = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"),
            "*.sql", SearchOption.TopDirectoryOnly);
        foreach (var procedure in procedures)
        {
            var sql = File.ReadAllText(procedure);
            Assert.DoesNotMatch(@"\b(?:BEGIN|COMMIT|ROLLBACK)\s+TRAN(?:SACTION)?\b", sql);
        }
    }

    [Fact]
    public void CommerceEngines_DoNotPollForCompletion()
    {
        var paths = new[] { Path.Combine("src", "API", "Auraly.Api", "DispatchSettlementHostedService.cs") };
        foreach (var path in paths)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot, path));
            Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Task.WhenAny", source, StringComparison.Ordinal);
            Assert.DoesNotContain("WaitForDocument", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DocumentsCompletedAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FiscalReturnsCompletedAsync", source, StringComparison.Ordinal);
        }

        var posReception = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence", "SqlPosSaleServerStore.cs"));
        Assert.DoesNotContain("FindAfterContentionAsync", posReception, StringComparison.Ordinal);
    }

    [Fact]
    public void Operational_document_handlers_do_not_write_financial_subledgers_or_ledger()
    {
        var persistence = Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence");
        var forbiddenWrites = new[]
        {
            "INSERT dbo.Receivables", "UPDATE dbo.Receivables",
            "INSERT dbo.ReceivableTransactions", "INSERT dbo.Payables",
            "UPDATE dbo.Payables", "INSERT dbo.PayableTransactions",
            "INSERT dbo.WorkSessionMovements", "INSERT dbo.AccountingEntries",
            "INSERT dbo.AccountingEntryLines"
        };
        var violations = Directory.GetFiles(
                persistence, "Sql*DocumentHandler*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path => forbiddenWrites
                .Where(token => File.ReadAllText(path).Contains(
                    token, StringComparison.OrdinalIgnoreCase))
                .Select(token => $"{Path.GetFileName(path)}: {token}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void SellerOrderInvoice_ConsumesReservedStockInTheInvoiceTransaction()
    {
        var checkout = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlOnlineSalesDraftStore.Checkout.cs"));
        var handler = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlPosSaleDocumentHandler.cs"));
        var ordersApi = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "OrdersApi.cs"));
        var batch = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Modules", "Orders", "Auraly.Application.Orders",
            "OrderBatchService.cs"));

        Assert.Contains("state.SourceOrderId", checkout, StringComparison.Ordinal);
        Assert.Contains("ResolveInventoryWarehouseAsync", handler, StringComparison.Ordinal);
        Assert.Contains("inventoryWarehouseId", handler, StringComparison.Ordinal);
        Assert.Contains("OnlineSalesCheckoutService checkout", batch, StringComparison.Ordinal);
        Assert.Contains("checkout.CompleteAsync", batch, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfirmedDocumentHandler", batch, StringComparison.Ordinal);
        Assert.DoesNotContain("SellerOrderInvoiceInventoryService", ordersApi, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "SellerOrderInvoiceInventoryService.cs")));
    }

    [Fact]
    public void DispatchSettlement_ExecutesCanonicalDocumentsWithoutWaitingForOtherEngines()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "DispatchSettlementHostedService.cs"));
        Assert.Contains("DocumentProcessingWorker", source, StringComparison.Ordinal);
        Assert.Contains("documentWorker.ProcessOneAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentProcessingJobs WHERE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FiscalStatus FROM", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyDboTableDebt_CannotGrow()
    {
        var count = SqlFiles(Path.Combine("database", "Auraly.Database", "Tables"))
            .Count(file => Regex.IsMatch(File.ReadAllText(file), @"CREATE\s+TABLE\s+\[?dbo\]?\.", RegexOptions.IgnoreCase));

        Assert.True(count <= 143,
            $"Legacy dbo tables grew to {count}. New module/catalog tables require an owned schema; DT-004 baseline is 143.");
    }

    [Fact]
    public void MigratedBusinessSelectors_DoNotReintroduceHardcodedLists()
    {
        var paths = new[]
        {
            "admin/src/app/(pos)/pos/pos-payment-dialog.tsx",
            "admin/src/app/(pos)/pos/pos-document-type-dialog.tsx",
            "admin/src/components/inventory/inventory-reason-master.tsx",
            "admin/src/app/(dashboard)/dashboard/agents/new/page.tsx",
            "admin/src/components/products/product-create-workspace.tsx",
            "admin/src/components/products/product-supplier-editor.tsx"
        };

        foreach (var path in paths)
            Assert.Contains("useReferenceOptions", File.ReadAllText(Path.Combine(RepositoryRoot, path)));
    }

    private static void AssertSingleClass(string className)
    {
        var matches = CSharpFiles("src")
            .Where(file => Regex.IsMatch(File.ReadAllText(file), $@"\bclass\s+{className}\b"))
            .ToArray();
        Assert.Single(matches);
    }

    private static IEnumerable<string> CSharpFiles(string relativePath) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, relativePath), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> SqlFiles(string relativePath) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, relativePath), "*.sql", SearchOption.AllDirectories);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
