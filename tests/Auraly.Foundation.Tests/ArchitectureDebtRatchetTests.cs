using System.Text.RegularExpressions;

namespace Auraly.Foundation.Tests;

public sealed class ArchitectureDebtRatchetTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Document_tax_snapshots_share_one_transactional_writer()
    {
        var insert = new Regex(@"\bINSERT(?:\s+INTO)?\s+\[?dbo\]?\.\[?DocumentWithholding(?:Snapshots|Lines)\]?\b",
            RegexOptions.IgnoreCase);
        var procedures = Directory.GetFiles(Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"), "*.sql");
        var owner = Assert.Single(CSharpFiles("src").Concat(procedures)
            .Where(file => insert.IsMatch(File.ReadAllText(file))));
        Assert.Equal("SqlWithholdingRuleStore.DocumentSnapshots.cs", Path.GetFileName(owner));
        Assert.Equal(2, insert.Matches(File.ReadAllText(owner)).Count);
        foreach (var producer in new[] { "SqlExpenseStore.Withholding.cs", "SqlGoodsReceiptDocumentHandler.cs", "SqlPosSaleDocumentHandler.cs" })
            Assert.Contains("SqlWithholdingRuleStore.InsertDocumentSnapshotsAsync", File.ReadAllText(
                Path.Combine(RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence", producer)));
    }

    [Fact]
    public void Portfolio_payment_numbering_reuses_the_existing_document_allocator()
    {
        foreach (var store in new[] { "SqlPayablesStore.cs", "SqlReceivablesStore.cs", "SqlPurchaseReturnStore.cs", "SqlSalesReturnStore.cs" })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Infrastructure",
                "Auraly.Infrastructure.Persistence", store));
            Assert.Contains("SqlOperationalDocumentAllocator.AllocateExistingNumberAsync", source);
            Assert.DoesNotContain("DocumentSeriesCursors", source);
            Assert.DoesNotContain("FROM dbo.DocumentSeries", source);
        }
    }

    [Fact]
    public void Portfolio_payment_headers_tenders_and_reservations_have_one_acceptance_owner()
    {
        var insert = new Regex(@"\bINSERT(?:\s+INTO)?\s+\[?dbo\]?\.\[?(?:SupplierPayments|CustomerPayments|SupplierPaymentTenders|CustomerPaymentTenders|SupplierPaymentApplications|CustomerPaymentApplications)\]?\b",
            RegexOptions.IgnoreCase);
        var procedures = Directory.GetFiles(Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"), "*.sql");
        var owner = Assert.Single(CSharpFiles("src").Concat(procedures)
            .Where(file => insert.IsMatch(File.ReadAllText(file))));
        Assert.Equal("PortfolioPaymentAccept.sql", Path.GetFileName(owner));
    }

    [Theory]
    [InlineData("SourcePartyIndex", "ManualVoucherPartyReferences")]
    [InlineData("SourcePositionIndex", "ManualVoucherCreditTargets")]
    public void Source_read_indexes_have_only_the_acceptance_writer_and_their_derived_backfill(string table, string parser)
    {
        var write = new Regex($@"\b(?:INSERT(?:\s+INTO)?|UPDATE|DELETE(?:\s+FROM)?)\s+\[?accounting\]?\.\[?{table}\]?\b", RegexOptions.IgnoreCase);
        var schemaRoot = Path.Combine(RepositoryRoot, "database", "Auraly.Database");
        var sqlFiles = Directory.GetFiles(schemaRoot, "*.sql", SearchOption.AllDirectories).Where(file => {
            var relative = Path.GetRelativePath(schemaRoot, file);
            return !relative.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !relative.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        });
        var files = CSharpFiles("src").Concat(sqlFiles);
        var writers = files.Where(file => write.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(new[] { "20261021_IndexManualVoucherParties.sql", "AccountingSourcesAccept.sql" }, writers);
        Assert.Contains("accounting." + parser, File.ReadAllText(Path.Combine(schemaRoot, "StoredProcedures", "AccountingSourcesAccept.sql")));
        Assert.Contains("accounting." + parser, File.ReadAllText(Path.Combine(schemaRoot, "Scripts", "Migrations", "20261021_IndexManualVoucherParties.sql")));
    }

    [Fact]
    public void Portfolio_obligations_transactions_and_credits_are_written_only_by_the_accounting_processor()
    {
        const string tables = @"(?:Payables|Receivables|PayableTransactions|ReceivableTransactions|SupplierCredits|CustomerCredits|SupplierCreditTransactions|CustomerCreditTransactions)";
        var directWrite = new Regex($@"\b(?:INSERT(?:\s+INTO)?|UPDATE|DELETE(?:\s+FROM)?)\s+\[?dbo\]?\.\[?{tables}\]?\b",
            RegexOptions.IgnoreCase);
        var aliasWrite = new Regex($@"\bUPDATE\s+(?<alias>\w+)\s+SET(?:(?!;).)*?\b(?:FROM|JOIN)\s+\[?dbo\]?\.\[?{tables}\]?\s+\k<alias>\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var processorDirectory = Path.Combine(RepositoryRoot, "src", "Modules", "Accounting", "Auraly.Commerce.Accounting.Infrastructure");
        var runtimeFiles = CSharpFiles("src").Concat(Directory.GetFiles(
            Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"), "*.sql"));
        var violations = runtimeFiles.Select(file => (Path: file, Text: File.ReadAllText(file)))
            .Where(file => (directWrite.IsMatch(file.Text) || aliasWrite.IsMatch(file.Text)) &&
                (Path.GetDirectoryName(file.Path) != processorDirectory || !Path.GetFileName(file.Path).StartsWith("SqlAccountingPostingProcessor", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file.Path));
        Assert.Empty(violations);
    }

    [Fact]
    public void Native_and_manual_obligations_share_one_opening_insert_per_subledger()
    {
        var opening = new Regex(@"\bINSERT(?:\s+INTO)?\s+\[?dbo\]?\.\[?(?<kind>Payables|Receivables)\]?\b", RegexOptions.IgnoreCase);
        var procedures = Directory.GetFiles(Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"), "*.sql");
        var writes = CSharpFiles("src").Concat(procedures).SelectMany(file =>
            opening.Matches(File.ReadAllText(file)).Select(match => (File: Path.GetFileName(file), Kind: match.Groups["kind"].Value))).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Single(writes.Where(write => write.Kind.Equals("Payables", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(writes.Where(write => write.Kind.Equals("Receivables", StringComparison.OrdinalIgnoreCase)));
        Assert.All(writes, write => Assert.Equal("SqlAccountingPostingProcessor.FinancialEffects.cs", write.File));
    }

    [Theory]
    [InlineData("SupplierCredits")]
    [InlineData("CustomerCredits")]
    public void Credit_openings_have_one_insert_owner(string table)
    {
        var insert = new Regex($@"\bINSERT(?:\s+INTO)?\s+\[?dbo\]?\.\[?{table}\]?\b", RegexOptions.IgnoreCase);
        var procedures = Directory.GetFiles(Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"), "*.sql");
        var owner = Assert.Single(CSharpFiles("src").Concat(procedures).Where(file => insert.IsMatch(File.ReadAllText(file))));
        Assert.Equal("SqlAccountingPostingProcessor.FinancialEffects.cs", Path.GetFileName(owner));
        Assert.Single(insert.Matches(File.ReadAllText(owner)));
    }

    [Fact]
    public void Financial_customer_and_configuration_notices_have_one_outbox_writer()
    {
        var directory = Path.Combine(RepositoryRoot, "src", "Modules", "Accounting", "Auraly.Commerce.Accounting.Infrastructure");
        var insert = new Regex(@"\bINSERT(?:\s+INTO)?\s+\[?dbo\]?\.\[?PosSynchronizationOutboxMessages\]?\b", RegexOptions.IgnoreCase);
        var owner = Assert.Single(Directory.GetFiles(directory, "*.cs")
            .Where(file => insert.IsMatch(File.ReadAllText(file))));
        Assert.Equal("SqlAccountingPosSynchronizationOutbox.cs", Path.GetFileName(owner));
        Assert.Single(insert.Matches(File.ReadAllText(owner)));
    }

    [Fact]
    public void Accounting_acceptance_has_one_SQL_owner_for_sources_and_jobs()
    {
        var sourceInsert = new Regex(@"\bINSERT(?:\s+INTO)?\s+\[?dbo\]?\.\[?AccountingSourceDocuments\]?\b", RegexOptions.IgnoreCase);
        var jobInsert = new Regex(@"\bINSERT(?:\s+INTO)?\s+\[?dbo\]?\.\[?AccountingPostingJobs\]?\b", RegexOptions.IgnoreCase);
        var procedures = Directory.GetFiles(Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"), "*.sql");
        var owners = CSharpFiles("src").Concat(procedures).ToArray();
        var acceptance = Assert.Single(owners.Where(file => sourceInsert.IsMatch(File.ReadAllText(file))));
        Assert.Equal("AccountingSourcesAccept.sql", Path.GetFileName(acceptance));
        Assert.Single(sourceInsert.Matches(File.ReadAllText(acceptance)));
        Assert.Single(jobInsert.Matches(File.ReadAllText(acceptance)));

        // Explicit retry already supports proven legacy sources without a job.
        // Acceptance and command replay cannot invoke or duplicate that recovery.
        var recovery = Assert.Single(owners.Where(file => file != acceptance && jobInsert.IsMatch(File.ReadAllText(file))));
        Assert.Equal("SqlAccountingStore.cs", Path.GetFileName(recovery));
        Assert.Single(jobInsert.Matches(File.ReadAllText(recovery)));
        Assert.Contains("RecoverMissingPostingJobAsync", File.ReadAllText(recovery));
    }

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
    public void Purchase_support_sources_have_one_transactional_snapshot_writer()
    {
        var insert = new Regex(@"\bINSERT(?:\s+INTO)?\s+\[?fiscal\]?\.\[?PurchaseSupportFiscalSnapshots\]?\b",
            RegexOptions.IgnoreCase);
        var owner = Assert.Single(CSharpFiles("src").Where(file => insert.IsMatch(File.ReadAllText(file))));
        Assert.Equal("SqlFiscalDocumentStore.PurchaseSupport.cs", Path.GetFileName(owner));
        Assert.Single(insert.Matches(File.ReadAllText(owner)));
        Assert.DoesNotMatch(@"\bBeginTransaction(?:Async)?\s*\(", File.ReadAllText(owner));
        Assert.DoesNotMatch(@"\.Commit(?:Async)?\s*\(", File.ReadAllText(owner));
    }

    [Fact]
    public void Operational_stock_is_never_reconstructed_by_summing_inventory_movements()
    {
        var runtimeFiles = CSharpFiles("src").Concat(
            Directory.GetFiles(
                Path.Combine(RepositoryRoot, "database", "Auraly.Database", "StoredProcedures"),
                "*.sql",
                SearchOption.AllDirectories));
        var violations = runtimeFiles
            .Where(file => ReconstructsOperationalStock(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file))
            .ToArray();

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("SELECT SUM(QuantityChange) FROM dbo.InventoryMovements",true)]
    [InlineData("SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m",true)]
    [InlineData("WITH movements AS(SELECT QuantityChange FROM dbo.InventoryMovements) SELECT SUM(m.QuantityChange) FROM movements m",true)]
    [InlineData("SELECT SUM(o.QuantityChange) Quantity FROM inventory.MovementOrigins o",true)]
    [InlineData("SELECT SUM(o.QuantityChange) Quantity FROM inventory.MovementOrigins o WHERE o.ReferencedMovementId=@MovementId AND o.OriginId=@OriginId AND o.QuantityChange>0",false)]
    [InlineData("SELECT SUM(o.QuantityChange) Quantity FROM inventory.MovementOrigins o WHERE o.ReferencedMovementId=@MovementId AND o.OriginId=@OriginId AND o.QuantityChange>0; SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m",true)]
    public void Stock_reconstruction_guard_only_exempts_reference_bound_positive_origin_restorations(string sql,bool forbidden)
        => Assert.Equal(forbidden,ReconstructsOperationalStock(sql));

    private static readonly Regex OperationalStockSumPattern=new(
        @"SUM\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?QuantityChange\s*\)",RegexOptions.IgnoreCase);
    // This aggregate counts portions already restored to one referenced issue,
    // not operational stock. Everything else retains the original prohibition,
    // including a second stock sum elsewhere in the same source file.
    private static readonly Regex ReferencedOriginRestorationPattern=new(
        @"SUM\s*\(\s*(?<originAlias>[A-Za-z_][A-Za-z0-9_]*)\.QuantityChange\s*\)\s+[A-Za-z_][A-Za-z0-9_]*\s+FROM\s+inventory\.MovementOrigins\s+\k<originAlias>\s+(?:WITH\s*\([^)]*\)\s*)?WHERE\s+\k<originAlias>\.ReferencedMovementId\s*=\s*[@A-Za-z_][@A-Za-z0-9_.]*\s+AND\s+\k<originAlias>\.OriginId\s*=\s*[@A-Za-z_][@A-Za-z0-9_.]*\s+AND\s+\k<originAlias>\.QuantityChange\s*>\s*0\b",
        RegexOptions.IgnoreCase);
    private static bool ReconstructsOperationalStock(string text) =>
        OperationalStockSumPattern.IsMatch(ReferencedOriginRestorationPattern.Replace(text,string.Empty));

    [Fact]
    public void Sales_and_orders_share_the_canonical_inventory_demand_resolver()
    {
        var owners = new[]
        {
            Path.Combine("src", "Infrastructure", "Auraly.Infrastructure.Persistence",
                "SqlOnlineSalesDraftStore.cs"),
            Path.Combine("src", "Infrastructure", "Auraly.Infrastructure.Persistence",
                "SqlOnlineSalesDraftStore.InventoryValidation.cs"),
            Path.Combine("src", "Pos", "Auraly.Pos.Edge.Infrastructure", "PosCaptureService.cs"),
            Path.Combine("src", "API", "Auraly.Api", "SellerOrdersApi.cs")
        };
        foreach (var owner in owners)
            Assert.Contains(
                "InventoryDemandResolver.",
                File.ReadAllText(Path.Combine(RepositoryRoot, owner)),
                StringComparison.Ordinal);

        var resolver = Path.Combine(
            RepositoryRoot, "src", "Modules", "Inventory",
            "Auraly.Domain.Inventory", "InventoryDemandResolver.cs");
        var duplicatedArithmetic = new Regex(
            @"\.Quantity\s*\*\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?InventoryFactor",
            RegexOptions.IgnoreCase);
        var violations = CSharpFiles("src")
            .Where(file => !string.Equals(file, resolver, StringComparison.OrdinalIgnoreCase))
            .Where(file => duplicatedArithmetic.IsMatch(File.ReadAllText(file)))
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
    public void Inventory_average_cost_has_one_calculator_and_one_transactional_writer()
    {
        var sourceFiles = CSharpFiles("src").ToArray();
        var calculatorCallers = sourceFiles
            .Where(file => File.ReadAllText(file).Contains(
                "InventoryValuationCalculator.Calculate(", StringComparison.Ordinal))
            .ToArray();

        var caller = Assert.Single(calculatorCallers);
        Assert.Equal(
            Path.Combine(RepositoryRoot, "src", "Infrastructure",
                "Auraly.Infrastructure.Persistence", "SqlInventoryLedgerWriter.cs"),
            caller);

        var allSource = string.Join('\n', sourceFiles.Select(File.ReadAllText));
        Assert.DoesNotContain("WriteCalculatedAsync", allSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculatedInventoryLedgerPosting", allSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WeightedAverageCost.ApplyReceipt", allSource, StringComparison.Ordinal);
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

        var reporting = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlSalesReportingProcessor.cs"));
        Assert.DoesNotContain(
            "CompletedAt=SYSDATETIMEOFFSET()", reporting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CompletedAt=@CompletedAt", reporting, StringComparison.Ordinal);
    }

    [Fact]
    public void Seller_orders_require_the_explicit_customer_site_across_web_and_pos()
    {
        var sellerApi = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "SellerOrdersApi.cs"));
        var posSave = File.ReadAllText(Path.Combine(
            RepositoryRoot, "admin", "src", "services", "orders",
            "save-pos-order.ts"));
        var edgeProgram = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Host", "Program.cs"));
        var contextSql = File.ReadAllText(Path.Combine(
            RepositoryRoot, "database", "Auraly.Database", "StoredProcedures",
            "SellerOrderContextGet.sql"));

        Assert.Contains(
            "CreateSellerOrderRequest(Guid BusinessId, Guid WarehouseId, Guid CustomerId,\n        Guid PartySiteId",
            sellerApi.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains(
            "UpdateSellerOrderRequest(Guid CustomerId, Guid PartySiteId",
            sellerApi, StringComparison.Ordinal);
        Assert.Contains("if (!draft.customerPartySiteId)", posSave,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MapPosOrders", edgeProgram, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Host", "PosOrderEndpoints.cs")));
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Host", "PosOrderServerClient.cs")));
        Assert.Contains("site.PartySiteId=@SiteId", contextSql, StringComparison.Ordinal);
        Assert.DoesNotContain("@SiteId IS NULL", contextSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Orders_keep_the_server_as_owner_and_use_edge_as_the_installed_transport()
    {
        var page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "admin", "src", "app", "(pos)", "pos", "page.tsx"));
        var edgeClient = File.ReadAllText(Path.Combine(
            RepositoryRoot, "admin", "src", "services", "pos", "pos-edge-client.ts"));
        var onlineClient = File.ReadAllText(Path.Combine(
            RepositoryRoot, "admin", "src", "services", "pos", "online-pos-client.ts"));
        var edgeProgram = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Host", "Program.cs"));

        Assert.Contains("getOrderClient", page, StringComparison.Ordinal);
        Assert.Contains("return client;", page, StringComparison.Ordinal);
        Assert.Contains("/edge/v1/orders", edgeClient, StringComparison.Ordinal);
        Assert.Contains("api/commerce/v1/orders", edgeProgram, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            RepositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Host", "PosOrdersServerClient.cs")));
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "PosOrdersApi.cs")));
        Assert.Contains("loadCommerceOrderPrintBatch(orderIds)", onlineClient,
            StringComparison.Ordinal);
        Assert.Contains("for (const receipt of receipts)", onlineClient,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SellerOrderAndInventoryProcedures_ParticipateInTheCallerTransaction()
    {
        var sellerSource = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "API", "Auraly.Api", "SellerOrdersApi.cs"));
        Assert.Contains("Procedure(\"dbo.SellerOrderCreate\",connection,transaction)", sellerSource, StringComparison.Ordinal);
        Assert.Contains("FindEditableAsync(connection,transaction", sellerSource, StringComparison.Ordinal);
        Assert.Contains("ConfirmSystemTransferAtomicallyAsync", sellerSource, StringComparison.Ordinal);
        Assert.Contains("P(\"@Status\",review?5:2)", sellerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SellerOrderConfirm", sellerSource, StringComparison.Ordinal);

        var editableOrderSql = File.ReadAllText(Path.Combine(
            RepositoryRoot, "database", "Auraly.Database", "StoredProcedures", "SellerOrderEditableGet.sql"));
        Assert.Contains("WITH(UPDLOCK,HOLDLOCK)", editableOrderSql, StringComparison.Ordinal);
        Assert.Contains("ReservedQuantity", editableOrderSql, StringComparison.Ordinal);

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
    public void Online_product_search_uses_one_sql_command_and_the_canonical_price_resolver()
    {
        var path = Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlOnlineSalesDraftStore.Temporaries.cs");
        var source = File.ReadAllText(path);
        var start = source.IndexOf(
            "public Task<OnlineSalesProductPage> SearchProductsAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private sealed record SearchProductCandidate(",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var search = source[start..end];

        Assert.Contains("SearchProductsCoreAsync(user, request, null, null, null,",
            search, StringComparison.Ordinal);
        Assert.Contains("public async Task<OnlineSalesProductPage> SearchAsync(",
            search, StringComparison.Ordinal);
        Assert.Contains("dbo.OnlineSalesProductSearch", search, StringComparison.Ordinal);
        Assert.Contains("CommandType.StoredProcedure", search, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(search, @"ExecuteReaderAsync\s*\("));
        Assert.Contains("CommercePriceResolver.Resolve(", search, StringComparison.Ordinal);
        Assert.Contains("independentLines: true", search, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadProductAsync(", search, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveCommercePricesAsync(", search, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransactionAsync(", search, StringComparison.Ordinal);

        var draftStore = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlOnlineSalesDraftStore.cs"));
        var updateStart = draftStore.IndexOf(
            "public async Task<OnlineSalesDraft> UpdateLinesAsync(",
            StringComparison.Ordinal);
        var updateEnd = draftStore.IndexOf(
            "public async Task<OnlineSalesDraft> RemoveLineAsync(",
            updateStart,
            StringComparison.Ordinal);
        var update = draftStore[updateStart..updateEnd];
        Assert.Contains("ReadLineProductsAsync(", update, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadProductsAsync(", update, StringComparison.Ordinal);
        Assert.Contains("OPENJSON(@UpdatesJson)", update, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", update, StringComparison.Ordinal);

        var temporaryStart = source.IndexOf(
            "public async Task<IReadOnlyList<OnlineSalesDraft>> ListTemporariesAsync(",
            StringComparison.Ordinal);
        var temporaryEnd = source.IndexOf(
            "public async Task<OnlineSalesDraft> PauseAsync(",
            temporaryStart,
            StringComparison.Ordinal);
        var temporaries = source[temporaryStart..temporaryEnd];
        Assert.Contains("ReadDraftsAsync(", temporaries, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", temporaries, StringComparison.Ordinal);

        var catalogStore = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Infrastructure", "PosCatalogStore.cs"));
        var catalogBatchStart = catalogStore.IndexOf(
            "public async Task<IReadOnlyDictionary<Guid, PosCatalogItem>> GetByProductIdsAsync(",
            StringComparison.Ordinal);
        var catalogBatchEnd = catalogStore.IndexOf(
            "public async Task<IReadOnlyDictionary<Guid, decimal>> InventoryFamilyAsync(",
            catalogBatchStart,
            StringComparison.Ordinal);
        var catalogBatch = catalogStore[catalogBatchStart..catalogBatchEnd];
        Assert.Single(Regex.Matches(catalogBatch, @"ExecuteReaderAsync\s*\("));
        Assert.Contains("json_each(@ProductIdsJson)", catalogBatch, StringComparison.Ordinal);

        var posDraftStore = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Infrastructure", "PosDraftStore.cs"));
        var updateLinesStart = posDraftStore.IndexOf(
            "public async Task<PosDraft> UpdateLinesAsync(",
            StringComparison.Ordinal);
        var updateLinesEnd = posDraftStore.IndexOf(
            "public async Task<PosDraft> RemoveLineAsync(",
            updateLinesStart,
            StringComparison.Ordinal);
        var updateLines = posDraftStore[updateLinesStart..updateLinesEnd];
        Assert.Contains("json_each(@UpdatesJson)", updateLines, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(updateLines, @"ExecuteAsync\s*\("));

        var repriceStart = posDraftStore.IndexOf(
            "public async Task<PosDraft> AssignCustomerAndPricesAsync(",
            StringComparison.Ordinal);
        var repriceEnd = posDraftStore.IndexOf(
            "public async Task<PosDraft> SaveTemporaryAsync(",
            repriceStart,
            StringComparison.Ordinal);
        var reprice = posDraftStore[repriceStart..repriceEnd];
        Assert.Contains("json_each(@PricesJson)", reprice, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(reprice, @"ExecuteAsync\s*\("));

        var importStart = posDraftStore.IndexOf(
            "public async Task<PosDraft> ImportOrderAsync(",
            StringComparison.Ordinal);
        var importEnd = posDraftStore.IndexOf(
            "public async Task<PosDraft> SetQuantityAsync(",
            importStart,
            StringComparison.Ordinal);
        var importOrder = posDraftStore[importStart..importEnd];
        Assert.Contains("FROM json_each(@LinesJson)", importOrder,
            StringComparison.Ordinal);
        Assert.Single(Regex.Matches(importOrder, @"FROM\s+json_each\(@LinesJson\)"));
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
        var directOrderCheckout = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlOnlineSalesDraftStore.OrderCheckout.cs"));

        Assert.DoesNotContain("ReleaseOrderInventoryAsync", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareSourceOrderInventoryAsync", batch, StringComparison.Ordinal);
        Assert.Contains("ResolveInventoryWarehouseAsync", handler, StringComparison.Ordinal);
        Assert.Contains("orders.OrdersWarehouseId", handler, StringComparison.Ordinal);
        Assert.Contains("inventoryWarehouseId", handler, StringComparison.Ordinal);
        Assert.Contains("InventoryConsumedByInvoice", handler, StringComparison.Ordinal);
        Assert.Contains("OnlineSalesCheckoutService checkout", batch, StringComparison.Ordinal);
        Assert.Contains("checkout.CompleteOrderAsync", batch, StringComparison.Ordinal);
        Assert.Contains("OnlineSalesOrderCheckoutSource", batch, StringComparison.Ordinal);
        Assert.DoesNotContain("OnlineSalesDraftService", batch, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderRecoveryService", batch, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteKnownDraftAsync", batch, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.SalesDrafts", directOrderCheckout, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.SalesDraftLines", directOrderCheckout, StringComparison.Ordinal);
        Assert.Contains("SourceOrderId", directOrderCheckout, StringComparison.Ordinal);
        Assert.Contains("ProgressCheckpointSize", batch, StringComparison.Ordinal);
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
    public void OnlineInvoiceBatch_ReusesFiscalMaterialAndResolvesItsContextInOneQuery()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Modules", "Sales", "Auraly.Application.Sales",
            "OnlineSalesCheckoutService.cs"));
        var checkout = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlOnlineSalesDraftStore.Checkout.cs"));
        var reception = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Modules", "Sales", "Auraly.Application.Sales",
            "ReceivePosSaleService.cs"));
        var batch = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Modules", "Orders", "Auraly.Application.Orders",
            "OrderBatchService.cs"));
        var directOrderCheckout = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence",
            "SqlOnlineSalesDraftStore.OrderCheckout.cs"));

        Assert.Contains("_fiscalMaterialByReference", service, StringComparison.Ordinal);
        Assert.Contains("_fiscalKeyContextByBusiness", service, StringComparison.Ordinal);
        Assert.Contains("FiscalAuthorizations fiscalAuthorization", directOrderCheckout,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FiscalAuthorizations authorization", directOrderCheckout,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("d.BusinessId", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SELECT BusinessId FROM dbo.SalesDrafts",
            checkout,
            StringComparison.Ordinal);
        var receiveCoreStart = reception.IndexOf(
            "private async Task<PosSaleUploadResponse> ReceiveCoreAsync(",
            StringComparison.Ordinal);
        var receiveCoreEnd = reception.IndexOf(
            "private static void ValidateIdempotencyKey(",
            receiveCoreStart,
            StringComparison.Ordinal);
        var receiveCore = reception[receiveCoreStart..receiveCoreEnd];
        Assert.Single(Regex.Matches(receiveCore, @"store\.FindAsync\s*\("));
    }

    [Fact]
    public void OrderBatch_LoadsOrdersAsOneBoundedSet_WithoutPerOrderCustomerReads()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Modules", "Orders", "Auraly.Application.Orders",
            "OrderBatchService.cs"));

        Assert.Contains("orders.GetBatchAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("orders.GetAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCustomerAsync(", source, StringComparison.Ordinal);
        Assert.Contains("request.OrderIds.Count is < 1 or > 50", source, StringComparison.Ordinal);
        var invoiceAsync = source.IndexOf(
            "public async Task<InvoiceOrdersResponse> InvoiceAsync(",
            StringComparison.Ordinal);
        Assert.True(invoiceAsync >= 0);
        var begin = source.IndexOf("batches.BeginAsync", invoiceAsync, StringComparison.Ordinal);
        Assert.True(begin >= 0);
        Assert.True(begin < source.IndexOf(
            "checkout.ValidateOrderCreditBatchAsync",
            invoiceAsync,
            StringComparison.Ordinal));
        Assert.True(begin < source.IndexOf(
            "orders.GetBatchAsync",
            invoiceAsync,
            StringComparison.Ordinal));
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

    [Fact]
    public void Frontend_does_not_introduce_periodic_intervals()
    {
        var violations = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "admin", "src"), "*.*",
                SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"(?<![A-Za-z0-9_])(?:window\.)?setInterval\s*\("))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/'))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Frontend_realtime_channels_use_the_shared_bounded_reconnect_policy()
    {
        var channelOwners = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "admin", "src"), "*.*",
                SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("synchronization/negotiate", StringComparison.Ordinal) &&
                       source.Contains("new WebSocket", StringComparison.Ordinal);
            })
            .ToArray();

        // The kitchen display has its own live channel alongside the three POS channels.
        Assert.Equal(4, channelOwners.Length);
        foreach (var owner in channelOwners)
        {
            var source = File.ReadAllText(owner);
            Assert.Contains("realtimeReconnectDelay", source, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"setTimeout\s*\(\s*\(\)\s*=>\s*void\s+connect\(\)\s*,\s*\d", source);
        }
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
