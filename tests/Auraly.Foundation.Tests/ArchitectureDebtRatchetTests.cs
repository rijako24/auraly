using System.Text.RegularExpressions;

namespace Auraly.Foundation.Tests;

public sealed class ArchitectureDebtRatchetTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void InventoryLedger_HasOneMovementWriter_AndBalanceProvisioningIsBounded()
    {
        var files = CSharpFiles("src");
        var ledgerWriterPattern = new Regex(
            @"\b(?:INSERT(?:\s+INTO)?\s+dbo\.InventoryMovements|UPDATE\s+dbo\.InventoryBalances)\b",
            RegexOptions.IgnoreCase);
        var ledgerWriters = files.Where(file => ledgerWriterPattern.IsMatch(File.ReadAllText(file)));

        var writer = Assert.Single(ledgerWriters);
        Assert.Equal(
            Path.Combine(RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence", "SqlInventoryLedgerWriter.cs"),
            writer);

        var balanceInsertPattern = new Regex(
            @"\bINSERT(?:\s+INTO)?\s+dbo\.InventoryBalances\b",
            RegexOptions.IgnoreCase);
        var balanceSqlOwners = files
            .Where(file => balanceInsertPattern.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "SqlBusinessDefaultsProvisioner.cs",
                "SqlCatalogStore.cs",
                "SqlInventoryLedgerWriter.cs",
                "SqlInventoryQueryStore.cs"
            },
            balanceSqlOwners);
    }

    [Fact]
    public void Operational_stock_is_never_reconstructed_by_summing_inventory_movements()
    {
        var runtimeFiles = CSharpFiles("src").Concat(
            Directory.GetFiles(
                Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"),
                "*.sql",
                SearchOption.AllDirectories));
        var forbidden = new Regex(
            @"SUM\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?QuantityChange\s*\)",
            RegexOptions.IgnoreCase);

        var violations = runtimeFiles
            .Where(file => forbidden.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file))
            .ToArray();

        Assert.Empty(violations);
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
    public void Pos_sale_processing_preserves_the_original_work_session()
    {
        var persistence = Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence");
        var handler = File.ReadAllText(Path.Combine(
            persistence, "SqlPosSaleDocumentHandler.cs"));
        var workSession = File.ReadAllText(Path.Combine(
            persistence, "SqlPosSaleDocumentHandler.WorkSession.cs"));

        Assert.Contains(
            "ValidateWorkSessionAsync(session, request, cancellationToken)",
            handler,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "request = request with { WorkSessionId",
            handler,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "INSERT dbo.WorkSessions",
            workSession,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "SELECT TOP(1) WorkSessionId",
            workSession,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "link.Parameters.AddWithValue(\"@WorkSessionId\", request.WorkSessionId)",
            workSession,
            StringComparison.Ordinal);
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
    public void Api_DoesNotEmbedDirectSql()
    {
        foreach (var file in CSharpFiles(Path.Combine("src", "API", "Auraly.Api")))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotMatch(@"(?:new\s+)?SqlCommand\s*\(", source);
            Assert.DoesNotMatch(@"\b(?:SELECT|INSERT|UPDATE|DELETE|MERGE)\s+(?:INTO\s+)?dbo\.", source);
            Assert.DoesNotContain("CommandText =", source, StringComparison.Ordinal);
        }
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
        Assert.Contains("ConfirmSystemTransferAtomicallyAsync", sellerSource, StringComparison.Ordinal);
        Assert.Contains("Procedure(\"dbo.SellerOrderConfirm\",connection,transaction)", sellerSource, StringComparison.Ordinal);

        var checkoutReleaseSource = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlOnlineSalesDraftStore.OrderInventory.cs"));
        Assert.Contains("ConfirmSystemTransferAtomicallyAsync", checkoutReleaseSource, StringComparison.Ordinal);
        Assert.Contains("ReleaseTransferId", checkoutReleaseSource, StringComparison.Ordinal);
        Assert.Contains("GROUP BY item.ProductId", checkoutReleaseSource, StringComparison.Ordinal);

        var saleHandlerSource = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlPosSaleDocumentHandler.cs"));
        Assert.DoesNotContain("\"TransferOut\"", saleHandlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TransferIn\"", saleHandlerSource, StringComparison.Ordinal);

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
        var orderInventory = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlOnlineSalesDraftStore.OrderInventory.cs"));
        var handler = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlPosSaleDocumentHandler.cs"));
        var ordersApi = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "OrdersApi.cs"));
        var batch = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Modules", "Orders", "Auraly.Application.Orders",
            "OrderBatchService.cs"));

        Assert.Contains("state.SourceOrderId", checkout, StringComparison.Ordinal);
        Assert.Contains("ReleaseOrderInventoryAsync", orderInventory, StringComparison.Ordinal);
        Assert.Contains("ConfirmSystemTransferAtomicallyAsync", orderInventory, StringComparison.Ordinal);
        Assert.Contains("InventoryReleasedForInvoice", orderInventory, StringComparison.Ordinal);
        Assert.Contains("InventoryConsumedByInvoice", handler, StringComparison.Ordinal);
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
        var posPaths = new[]
        {
            "admin/src/app/(pos)/pos/pos-payment-dialog.tsx",
            "admin/src/app/(pos)/pos/pos-document-type-dialog.tsx"
        };
        foreach (var path in posPaths)
            Assert.Contains("usePosReferenceOptions", File.ReadAllText(Path.Combine(RepositoryRoot, path)));

        var adminPaths = new[]
        {
            "admin/src/components/inventory/inventory-reason-master.tsx",
            "admin/src/app/(dashboard)/dashboard/agents/new/page.tsx",
            "admin/src/components/products/product-create-workspace.tsx",
            "admin/src/components/products/product-supplier-editor.tsx"
        };

        foreach (var path in adminPaths)
            Assert.Contains("useReferenceOptions", File.ReadAllText(Path.Combine(RepositoryRoot, path)));
    }

    [Fact]
    public void SqlFactories_ConsumeTheCanonicalConnectionSource()
    {
        var paths = new[]
        {
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlServerConnectionFactory.cs",
            "src/Modules/Accounting/Auraly.Commerce.Accounting.Infrastructure/AccountingSqlConnectionFactory.cs",
            "src/Modules/Payroll/Auraly.Commerce.Payroll.Infrastructure/PayrollSqlConnectionFactory.cs",
            "src/Modules/Pricing/Auraly.Infrastructure.Pricing/PricingSqlConnectionFactory.cs",
            "src/Modules/Routes/Auraly.Infrastructure.Routes/RoutesSqlConnectionFactory.cs",
            "src/Modules/Dispatching/Auraly.Infrastructure.Dispatching/DispatchingSqlConnectionFactory.cs"
        };

        foreach (var path in paths)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot, path));
            Assert.Contains("AuralySqlConnectionSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("string connectionString", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExecutionContext_EmitsTenantAwareRequestTelemetry()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "ExecutionContextMiddleware.cs"));

        Assert.Contains("auraly.tenant.id", source, StringComparison.Ordinal);
        Assert.Contains("auraly.business.id", source, StringComparison.Ordinal);
        Assert.Contains("TenantRequestCompleted", source, StringComparison.Ordinal);
        Assert.Contains("ElapsedMilliseconds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseOrdersAndRotation_DoNotEmbedSqlInApplicationCode()
    {
        var paths = new[]
        {
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlPurchaseOrderStore.cs",
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlCatalogStore.Rotation.cs",
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlGoodsReceiptStore.cs",
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlGoodsReceiptDocumentHandler.cs",
            "src/Infrastructure/Auraly.Infrastructure.Persistence/SqlSalesReportingProjectionWriter.cs"
        };

        foreach (var path in paths)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot, path));
            Assert.DoesNotContain("purchasing.PurchaseOrders ", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("purchasing.PurchaseOrderLines ", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reporting.ProductRotationSnapshots ", source, StringComparison.OrdinalIgnoreCase);
        }

        var procedures = new[]
        {
            "PurchasingPurchaseOrdersList.sql", "PurchasingPurchaseOrderGet.sql",
            "PurchasingPurchaseOrderDraftSave.sql", "PurchasingPurchaseOrderConfirm.sql",
            "PurchasingPurchaseOrderClose.sql", "PurchasingReceiptOrderValidate.sql",
            "PurchasingReceiptOrderFulfillmentApply.sql", "ReportingProductRotationRefresh.sql",
            "CatalogProductRotationGet.sql"
        };
        foreach (var procedure in procedures)
            Assert.True(File.Exists(Path.Combine(RepositoryRoot, "database", "Auraly.Database",
                "StoredProcedures", procedure)), $"Missing procedure {procedure}.");
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
