using System.Diagnostics;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Application.Payables;
using Auraly.Application.Receivables;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(PortfolioManualPaymentPerformanceCollection.Name)]
[Trait("Performance", "PortfolioPaymentHistory")]
public sealed class PortfolioPaymentHistoryPerformanceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    // A bounded fixture does not sort the cross product of the database's
    // system catalog just to produce 10,000 numbers. Setup volume stays exact
    // regardless of how many schema objects the installed version contains.
    private const string TenThousandNumbersSql = """
        (SELECT 1+thousands.N*1000+hundreds.N*100+tens.N*10+ones.N AS Number
         FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) thousands(N)
         CROSS JOIN (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) hundreds(N)
         CROSS JOIN (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) tens(N)
         CROSS JOIN (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) ones(N)) numbers
        """;
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task History_pages_filter_before_loading_details_with_ten_thousand_payments(bool supplier) =>
        RunReadVolumeScenarioAsync(supplier, creditReportOnly:false);

    [Fact]
    public Task Credit_report_pages_preserve_totals_with_ten_thousand_positions_and_credits() =>
        RunReadVolumeScenarioAsync(supplier:false, creditReportOnly:true);

    private async Task RunReadVolumeScenarioAsync(bool supplier,bool creditReportOnly)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var prefix = "HIST-" + Guid.NewGuid().ToString("N")[..8] + "-";
        var roleId = supplier ? fixture.SupplierId : Guid.NewGuid();
        Guid siteId;
        var role = supplier ? "Supplier" : "Customer";
        var position = supplier ? "Payable" : "Receivable";
        var otherRoleColumns = supplier ? "" : ",PartyId";
        var otherRoleValues = supplier ? "" : ",NULL";
        // Synthetic read-volume fixtures: one immutable opening and one accepted
        // native payment/application per invoice. No live or production database.
        await using (var seed = new SqlCommand($"""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @Site uniqueidentifier=(SELECT PartySiteId FROM dbo.PartySites WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1);
            IF @Supplier=0
            BEGIN
              DECLARE @CustomerParty uniqueidentifier=NEWID(),@CustomerSite uniqueidentifier=NEWID();
              INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerParty,@TenantId,N'Organization',N'Cliente de volumen de historial',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
              INSERT dbo.Customers(CustomerId,PartyId,TenantId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
                VALUES(@RoleId,@CustomerParty,@TenantId,0,1,@UserId,SYSDATETIMEOFFSET());
              INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                SELECT @CustomerSite,@CustomerParty,N'PRINCIPAL',N'Principal',CountryId,AdministrativeDivisionId,CityId,AddressLine,1,1,@UserId,SYSDATETIMEOFFSET()
                FROM dbo.PartySites WHERE PartySiteId=@Site;
              SET @Site=@CustomerSite;
            END;
            -- Give the 10,000-row bulk setup real cardinality statistics;
            -- this setup is outside the timed reads.
            CREATE TABLE #Seed(Number int PRIMARY KEY,PositionId uniqueidentifier,SourceId uniqueidentifier,JobId uniqueidentifier,PaymentId uniqueidentifier);
            INSERT #Seed SELECT Number,NEWID(),NEWID(),NEWID(),NEWID()
              FROM {TenThousandNumbersSql};
            IF (SELECT COUNT(*) FROM #Seed)<>10000 THROW 51999,'The volume fixture requires exactly 10,000 positions.',1;
            DECLARE @At datetimeoffset(7)='2026-08-01T12:00:00+00:00',@Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),'[]'));
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT SourceId,N'HistoryVolumeFixture',@TenantId,@BusinessId,N'[]',@Hash,@At,@At,0 FROM #Seed;
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourcePayloadHash,
              OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
            SELECT JobId,@TenantId,@BusinessId,SourceId,N'HistoryVolumeFixture',@Hash,@At,0,N'Posted',1,@At,@At FROM #Seed;
            INSERT dbo.{position}s({position}Id,BusinessId,{role}Id,PartySiteId,SourceDocumentId,SourceDocumentType,
              DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            SELECT PositionId,@BusinessId,@RoleId,@Site,SourceId,N'HistoryVolumeFixture',CONCAT(@Prefix,Number),N'COP',100,100,
              DATEADD(day,30,@At),N'Open',@At FROM #Seed;
            INSERT dbo.{role}Payments(PaymentId,BusinessId,{role}Id{otherRoleColumns},DocumentSeriesId,DocumentNumber,DocumentPrefix,DocumentSeriesCode,
              DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,TotalAmount,Status,ConfirmedByUserId,AcceptedAt)
            SELECT PaymentId,@BusinessId,@RoleId{otherRoleValues},@SeriesId,CONCAT(@Prefix,Number),N'HIST',N'HIST',Number,
              CONCAT(@Prefix,Number),@Hash,DATEADD(day,CASE WHEN Number>9900 THEN 1 ELSE 0 END,@At),N'COP',1,N'Accepted',@UserId,@At FROM #Seed;
            INSERT dbo.{role}PaymentTenders(PaymentId,LineNumber,MethodCode,Amount) SELECT PaymentId,1,N'Cash',1 FROM #Seed;
            INSERT dbo.{role}PaymentApplications(PaymentId,LineNumber,{position}Id,Amount) SELECT PaymentId,1,PositionId,1 FROM #Seed;
            COMMIT; SELECT @Site;
            """, connection) { CommandTimeout = 120 })
        {
            seed.Parameters.AddWithValue("@Supplier", supplier); seed.Parameters.AddWithValue("@RoleId", roleId);
            seed.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId); seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId); seed.Parameters.AddWithValue("@UserId", fixture.UserId);
            seed.Parameters.AddWithValue("@SeriesId", fixture.OnlineDocumentSeriesId); seed.Parameters.AddWithValue("@Prefix", prefix);
            try {siteId = (Guid)(await seed.ExecuteScalarAsync())!;}
            catch(SqlException error)
            {
                foreach(SqlError detail in error.Errors)
                    output.WriteLine("Read-volume preparation SQL {0}, severity {1}, state {2}, line {3}: {4}",
                        detail.Number,detail.Class,detail.State,detail.LineNumber,detail.Message);
                throw;
            }
        }
        using var scope = fixture.CreateScope();
        var payableStore = scope.ServiceProvider.GetRequiredService<IPayablesStore>();
        var receivableStore = scope.ServiceProvider.GetRequiredService<IReceivablesStore>();
        var permissions = new HashSet<string> { PayablesPermissionCodes.Read, ReceivablesPermissionCodes.Read };
        var payableUser = new PayablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId, permissions);
        var receivableUser = new ReceivablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId, permissions);
        if(!creditReportOnly)
        foreach (var filtered in new[] { false, true })
        foreach (var pageNumber in new[] { 1, 2 })
        {
            var samples = new List<double>();
            Guid[]? firstPage = null;
            for (var sample = 0; sample < 20; sample++)
            {
                var from = filtered ? new DateOnly(2026, 8, 2) : (DateOnly?)null;
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var timer = Stopwatch.StartNew();
                Guid[] ids;
                if (supplier)
                {
                    var result = await payableStore.ListPaymentsAsync(payableUser,
                        new(pageNumber, 25, prefix, roleId, From: from, PartySiteId: siteId, DateRangeVersion: 2), default);
                    Assert.Equal(filtered ? 100 : 10000, result.TotalCount);
                    Assert.Equal(25, result.Items.Count);
                    Assert.All(result.Items, item => { Assert.Single(item.Payments); Assert.Single(item.Applications); Assert.Equal(1m, item.TotalAmount); });
                    ids = result.Items.Select(item => item.PaymentId).ToArray();
                }
                else
                {
                    var result = await receivableStore.ListPaymentsAsync(receivableUser,
                        new(pageNumber, 25, prefix, roleId, From: from, PartySiteId: siteId, DateRangeVersion: 2), default);
                    Assert.Equal(filtered ? 100 : 10000, result.TotalCount);
                    Assert.Equal(filtered ? 100m : 10000m, result.TotalAmount);
                    Assert.Equal(25, result.Items.Count);
                    Assert.All(result.Items, item => { Assert.Single(item.Payments); Assert.Single(item.Applications); Assert.Equal(1m, item.TotalAmount); });
                    ids = result.Items.Select(item => item.PaymentId).ToArray();
                }
                timer.Stop();
                Assert.Equal(filtered ? 2 : 1, counter.Count);
                firstPage ??= ids;
                Assert.Equal(firstPage, ids); // Stable tie breaker despite identical dates.
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95 = samples.Order().ElementAt(18);
            output.WriteLine("{0} history: page={1}, dateFilter={2}, first={3:F1}ms, p95={4:F1}ms; SQL={5}; 10000 payments; 20 samples",
                position, pageNumber, filtered, samples[0], p95, filtered ? 2 : 1);
            Assert.True(p95 < 800, $"History exceeded its budget: {position}, page {pageNumber}, p95 {p95:F1}ms.");
        }

        // The staged page must retain every supported order, including the
        // amount scoped to this site. Exercise real SQL aliases, not UI mocks.
        if(!creditReportOnly)
        foreach(var sortBy in new[]{"partyName","documentNumber","totalAmount"})
        {
            using var reads=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
            (string Number,string? Name,decimal Amount)[] rows;
            if(supplier)
            {
                var ordered=await payableStore.ListPaymentsAsync(payableUser,
                    new(1,25,prefix,roleId,PartySiteId:siteId,SortBy:sortBy,SortDirection:"asc",DateRangeVersion:2),default);
                rows=ordered.Items.Select(item=>(item.DocumentNumber,item.SupplierName,item.TotalAmount)).ToArray();
                Assert.Equal(10000,ordered.TotalCount);
            }
            else
            {
                var ordered=await receivableStore.ListPaymentsAsync(receivableUser,
                    new(1,25,prefix,roleId,PartySiteId:siteId,SortBy:sortBy,SortDirection:"asc",DateRangeVersion:2),default);
                rows=ordered.Items.Select(item=>(item.DocumentNumber,item.CustomerName,item.TotalAmount)).ToArray();
                Assert.Equal(10000,ordered.TotalCount);Assert.Equal(10000m,ordered.TotalAmount);
            }
            Assert.Equal(1,reads.Count);Assert.Equal(25,rows.Length);
            if(sortBy=="totalAmount") Assert.Equal(rows.Select(item=>item.Amount).Order(),rows.Select(item=>item.Amount));
            else
            {
                var values=rows.Select(item=>sortBy=="documentNumber"?item.Number:item.Name).ToArray();
                Assert.Equal(values.Order(StringComparer.OrdinalIgnoreCase),values);
            }
        }

        // Reuse this isolated read-volume dataset for applied totals. Functional
        // manual/native tests exercise acceptance and its actual advance/ledger
        // effects; these synthetic facts exercise paging and historical readers.
        await using (var applied = new SqlCommand($"""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @At datetimeoffset(7)='2026-08-01T12:00:00+00:00',@Later datetimeoffset(7)='2026-08-03T12:00:00+00:00',
              @Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),'[]'));
            CREATE TABLE #Rows(PositionId uniqueidentifier PRIMARY KEY,PaymentId uniqueidentifier,PaidAt datetimeoffset(7),
              OriginalTransactionId uniqueidentifier,UnapplicationId uniqueidentifier,JobId uniqueidentifier,LineId uniqueidentifier);
            INSERT #Rows SELECT application.{position}Id,payment.PaymentId,payment.PaidAt,NEWID(),NEWID(),NEWID(),NEWID()
              FROM dbo.{role}Payments payment JOIN dbo.{role}PaymentApplications application ON application.PaymentId=payment.PaymentId
              WHERE payment.BusinessId=@BusinessId AND payment.IdempotencyKey LIKE @Prefix+N'%';
            IF @@ROWCOUNT<>10000 THROW 51500,'The applied read fixture is incomplete.',1;
            UPDATE payment SET Status=N'Processed',ProcessedAt=payment.PaidAt FROM dbo.{role}Payments payment JOIN #Rows input ON input.PaymentId=payment.PaymentId;
            UPDATE application SET AppliedAt=input.PaidAt FROM dbo.{role}PaymentApplications application JOIN #Rows input ON input.PaymentId=application.PaymentId;
            UPDATE balance SET OutstandingAmount=99.25,Status=N'PartiallyPaid' FROM dbo.{position}s balance JOIN #Rows input ON input.PositionId=balance.{position}Id;
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT UnapplicationId,N'ManualAccountingVoucher',@TenantId,@BusinessId,N'[]',@Hash,@Later,@Later,0 FROM #Rows;
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourcePayloadHash,
              OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
            SELECT JobId,@TenantId,@BusinessId,UnapplicationId,N'ManualAccountingVoucher',@Hash,@Later,0,N'CommercialEffectsApplied',1,@Later,@Later FROM #Rows;
            INSERT dbo.{position}Transactions({position}TransactionId,{position}Id,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT NEWID(),input.PositionId,N'Opening',100,balance.SourceDocumentId,@At,@At FROM #Rows input JOIN dbo.{position}s balance ON balance.{position}Id=input.PositionId;
            INSERT dbo.{position}Transactions({position}TransactionId,{position}Id,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT OriginalTransactionId,PositionId,N'Payment',1,PaymentId,PaidAt,PaidAt FROM #Rows;
            INSERT dbo.{position}Transactions({position}TransactionId,{position}Id,TransactionType,Amount,SourceDocumentId,SourceDocumentType,
              SourceLineId,ReversesTransactionId,OccurredAt,CreatedAt)
            SELECT NEWID(),PositionId,N'Unapplication',0.25,UnapplicationId,N'ManualAccountingVoucher',LineId,OriginalTransactionId,@Later,@Later FROM #Rows;
            COMMIT;
            """, connection) { CommandTimeout = 120 })
        {
            applied.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            applied.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            applied.Parameters.AddWithValue("@Prefix", prefix);
            await applied.ExecuteNonQueryAsync();
        }
        if(creditReportOnly)
        {
            await AssertCreditReportAsync(connection,receivableStore,receivableUser,roleId,siteId,prefix);
            return;
        }
        // The administrative selector reuses these applied facts. Its page
        // enrichment must stay bounded even for a third party with 10,000 debts.
        Guid partyId;
        await using (var party = new SqlCommand("SELECT PartyId FROM dbo.PartySites WHERE PartySiteId=@Site", connection))
        {
            party.Parameters.AddWithValue("@Site",siteId);
            partyId = (Guid)(await party.ExecuteScalarAsync())!;
        }
        var accounting = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var accountingUser = new AccountingUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,permissions);
        await using var selectorDiagnostic = new SqlCommand { Connection = connection };
        foreach (var pageNumber in new[] { 1,2 })
        {
            var samples = new List<double>();
            for (var sample = 0; sample < 20; sample++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    beforeExecute: command => {
                        if(command.CommandText.Contains("#OpenPositionCandidates",StringComparison.Ordinal))
                            CaptureReadCommand(command,selectorDiagnostic);
                    }, afterExecute: (_,elapsed) => {
                        if(sample==0) output.WriteLine($"Administrative selector supplier={supplier} page={pageNumber}: SQL first result {elapsed:F1}ms.");
                    });
                var timer = Stopwatch.StartNew();
                var result = await accounting.OpenPositionsAsync(accountingUser,new("All",pageNumber,25,PartyId:partyId,
                    Search:prefix,EffectiveOn:new(2026,9,11),VoucherTypeCode:supplier ? "Disbursement" : "CashReceipt"));
                timer.Stop();
                Assert.Equal(1,counter.Count);
                Assert.Equal(10_000,result.TotalCount);
                Assert.Equal(25,result.Items.Count);
                Assert.NotNull(result.ReferenceDate);
                var expectedAge = Math.Max(0,result.ReferenceDate.Value.DayNumber - new DateOnly(2026,8,31).DayNumber);
                Assert.All(result.Items,item => {
                    Assert.Equal(position,item.SubledgerKind);
                    Assert.Equal(100m,item.OriginalAmount);
                    Assert.Equal(0.75m,item.PaidAmount);
                    Assert.Equal(99.25m,item.OutstandingAmount);
                    Assert.Equal(0m,item.OtherMovementsAmount);
                    Assert.Equal(expectedAge,item.OverdueDays);
                });
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95 = samples.Order().ElementAt((int)Math.Ceiling(samples.Count * .95) - 1);
            output.WriteLine($"Administrative selector supplier={supplier} page={pageNumber}: SQL=1 first={samples[0]:F1}ms p95={p95:F1}ms.");
            if(samples[0]>=2_000 || p95>=1_500)
            {
                try { await CaptureReportProfileAsync(selectorDiagnostic); }
                catch(Exception diagnostic) { output.WriteLine($"Selector diagnostics unavailable: {diagnostic.Message}"); }
            }
            Assert.True(samples[0] < 2_000,$"Administrative selector first page took {samples[0]:F1}ms.");
            Assert.True(p95 < 1_500,$"Administrative selector p95 was {p95:F1}ms.");
        }
        var reportScenarios = new[] { "invoices-1", "invoices-2", "report-before", "report-after-1", "report-after-2", "report-compact-1", "report-compact-2" }
            .Concat(supplier ? [] : new[] { "report-filtered-1", "report-filtered-2" });
        await using var originPlanCommand = new SqlCommand { Connection = connection };
        foreach (var scenario in reportScenarios)
        {
            var samples = new List<double>();
            await using var diagnosticCommand = new SqlCommand { Connection = connection };
            for (var sample = 0; sample <= 20; sample++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    beforeExecute: command =>
                    {
                        // Capture only the warmup in memory. Profiling a failed
                        // read runs after all measured samples, never inside them.
                        if (sample == 0 && command.CommandText.Contains("INTO #Report", StringComparison.Ordinal))
                        {
                            CaptureReadCommand(command, diagnosticCommand);
                            if (scenario == "report-after-1") CaptureReadCommand(command, originPlanCommand);
                        }
                    });
                var timer = Stopwatch.StartNew();
                var pageNumber = scenario.EndsWith('2') ? 2 : 1;
                if (scenario.StartsWith("invoices"))
                {
                    if (supplier)
                    {
                        var result = await payableStore.ListAsync(payableUser,
                            new(pageNumber, 25, prefix, roleId, null, null, PartySiteId: siteId, DateRangeVersion: 2), default);
                        Assert.Equal(10000, result.TotalCount);
                        Assert.Equal(992_500m, result.TotalOutstanding);
                        Assert.All(result.Items, item => Assert.Equal(0.75m, item.PaidAmount));
                        Assert.Equal(25, result.Items.Count);
                    }
                    else
                    {
                        var result = await receivableStore.ListAsync(receivableUser,
                            new(pageNumber, 25, prefix, roleId, null, null, PartySiteId: siteId, DateRangeVersion: 2), default);
                        Assert.Equal(10000, result.TotalCount);
                        Assert.Equal(7_500m, result.TotalPaid);
                        Assert.Equal(992_500m, result.TotalOutstanding);
                        Assert.All(result.Items, item => Assert.Equal(0.75m, item.PaidAmount));
                        Assert.Equal(25, result.Items.Count);
                    }
                    // Business-local aging reads the time zone once, then aggregates and page.
                    Assert.Equal(3, counter.Count);
                }
                else
                {
                    var cutoff = scenario == "report-before" ? new DateOnly(2026, 8, 2) : new(2026, 8, 4);
                    var expected = scenario == "report-before" ? 1m : 0.75m;
                    var includeApplications = !scenario.StartsWith("report-compact", StringComparison.Ordinal);
                    if (supplier)
                    {
                        var result = await payableStore.ReportAsync(payableUser,
                            new(pageNumber, 25, false, cutoff, roleId, PartySiteId: siteId, Search: prefix, IncludeApplications: includeApplications, DateRangeVersion: 2), default);
                        Assert.Equal(10000, result.TotalCount);
                        Assert.Equal(expected * 10000, Assert.Single(result.CurrencyTotals).PaidAmount);
                        Assert.All(result.Items, item => { Assert.Equal(expected, item.PaidAmount); Assert.Equal(includeApplications ? expected : 0m, item.Applications!.Sum(row => row.Amount)); });
                        Assert.Equal(25, result.Items.Count);
                    }
                    else
                    {
                        var dayFilters = scenario.StartsWith("report-filtered",StringComparison.Ordinal);
                        var result = await receivableStore.ReportAsync(receivableUser,
                            new(pageNumber, 25, false, cutoff, roleId, PartySiteId: siteId, Search: prefix, IncludeApplications: includeApplications,
                                IssuedFrom:dayFilters ? new(2026,8,1) : null,IssuedTo:dayFilters ? new(2026,8,1) : null,
                                MinimumAgeDays:dayFilters ? 3 : null,MaximumAgeDays:dayFilters ? 3 : null,
                                MinimumDaysPastDue:dayFilters ? -27 : null,MaximumDaysPastDue:dayFilters ? -27 : null, DateRangeVersion: 2), default);
                        Assert.Equal(10000, result.TotalCount);
                        Assert.Equal(expected * 10000, result.TotalPaid);
                        Assert.All(result.Items, item => { Assert.Equal(expected, item.PaidAmount); Assert.Equal(includeApplications ? expected : 0m, item.Applications!.Sum(row => row.Amount)); });
                        Assert.Equal(25, result.Items.Count);
                        Assert.All(result.Items,item =>
                        {
                            Assert.Equal(10000,item.GroupTotals!.InvoiceCount);
                            Assert.Equal(1_000_000m-expected*10000,item.GroupTotals.OutstandingAmount);
                            Assert.Equal(30,item.TermDays);
                        });
                    }
                    // The existing report first resolves the business time zone,
                    // then reads its scoped aggregates/page/details in one command.
                    Assert.Equal(2, counter.Count);
                }
                timer.Stop();
                if (sample > 0) samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95 = samples.Order().ElementAt(18);
            output.WriteLine("{0} applied totals: {1}, p95={2:F1}ms; SQL={3}; 10000 payments/unapplications; 20 samples", position, scenario, p95, scenario.StartsWith("invoices")?3:2);
            if (p95 >= 1000)
            {
                try { await CaptureReportProfileAsync(diagnosticCommand); }
                catch (Exception diagnostic) { output.WriteLine($"Report diagnostics unavailable: {diagnostic.Message}"); }
            }
            Assert.True(p95 < 1000, $"Applied totals exceeded their budget: {position}, {scenario}, p95 {p95:F1}ms.");
        }
        // Verify actual work as well as wall time. A fast server must not hide
        // a full payment-history scan while enriching 25 invoices/50 applications.
        // This additional read runs only after every timed scenario has finished.
        var profileDirectory = await CaptureReportProfileAsync(originPlanCommand);
        Assert.NotNull(profileDirectory);
        var foundOrigins = false;
        foreach (var file in Directory.EnumerateFiles(profileDirectory, "profile-*.json"))
        {
            using var profile = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file));
            var operators = profile.RootElement.EnumerateArray().ToArray();
            if (operators.Length == 0 || !operators[0].GetProperty("StmtText").GetString()!
                    .Contains("FROM #PageApplications application", StringComparison.Ordinal)) continue;
            foundOrigins = true;
            var returned = operators[0].GetProperty("Rows").GetInt64();
            Assert.Equal(50, returned);
            var paymentReads = operators.Where(item => item.GetProperty("StmtText").GetString() is { } text
                && text.Contains("OBJECT:", StringComparison.Ordinal)
                && text.Contains($"[dbo].[{role}Payments]", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(paymentReads);
            Assert.All(paymentReads, item =>
            {
                Assert.Contains("Seek", item.GetProperty("PhysicalOp").GetString());
                Assert.InRange(item.GetProperty("Rows").GetInt64(), 0, returned);
            });
        }
        Assert.True(foundOrigins, "The actual plan must include receipt identity enrichment.");
        // The complete-export guard runs after the timed reads and must reject
        // the full filtered set, rather than silently returning its first page.
        if (supplier)
            await Assert.ThrowsAsync<PortfolioReportLimitException>(() =>
                scope.ServiceProvider.GetRequiredService<PayablesService>().PrintReportAsync(payableUser,
                    new(1, 25, false, new(2026, 8, 4), roleId, PartySiteId: siteId, Search: prefix, IncludeApplications: false)));
        else
            await Assert.ThrowsAsync<PortfolioReportLimitException>(() =>
                scope.ServiceProvider.GetRequiredService<ReceivablesService>().PrintReportAsync(receivableUser,
                    new(1, 25, false, new(2026, 8, 4), roleId, PartySiteId: siteId, Search: prefix, IncludeApplications: false)));
    }
    private async Task AssertCreditReportAsync(SqlConnection connection,IReceivablesStore store,
        ReceivablesUserIdentity user,Guid customerId,Guid siteId,string prefix)
    {
        // Read-volume fixtures only: the functional test creates and crosses
        // actual receipts. Here 10,000 existing positions receive 10,000 credits.
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using(var seed=new SqlCommand($$"""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @Source uniqueidentifier=NEWID(),@At datetimeoffset(7)='2026-08-01T12:00:00+00:00',
              @Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),'{}'));
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
              PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            VALUES(@Source,N'ManualAccountingVoucher',@Tenant,@Business,N'{}',@Hash,@At,@At,0);
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
              SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
            VALUES(NEWID(),@Tenant,@Business,@Source,N'ManualAccountingVoucher',@Hash,@At,0,N'CommercialEffectsApplied',1,@At,@At);
            CREATE TABLE #CreditSeed(N int PRIMARY KEY,CreditId uniqueidentifier,LineId uniqueidentifier);
            INSERT #CreditSeed SELECT Number,NEWID(),NEWID()
              FROM {{TenThousandNumbersSql}};
            IF (SELECT COUNT(*) FROM #CreditSeed)<>10000 THROW 51999,'The volume fixture requires exactly 10,000 credits.',1;
            INSERT dbo.CustomerCredits(CustomerCreditId,BusinessId,CustomerId,PartySiteId,OriginalAmount,AvailableAmount,
              Status,CreatedAt,SourceDocumentId,SourceDocumentType,SourceLineId,CreditNature,CurrencyCode)
            SELECT CreditId,@Business,@Customer,@Site,100,100,N'Open',@At,@Source,N'ManualAccountingVoucher',LineId,
              CASE WHEN N%2=0 THEN N'Advance' ELSE N'Credit' END,N'COP' FROM #CreditSeed;
            INSERT dbo.CustomerCreditTransactions(CustomerCreditTransactionId,CustomerCreditId,TransactionType,Amount,
              SourceDocumentId,SourceDocumentType,SourceLineId,OccurredAt,CreatedAt)
            SELECT NEWID(),CreditId,N'Opening',100,@Source,N'ManualAccountingVoucher',LineId,@At,@At FROM #CreditSeed;
            COMMIT;
            """,connection))
        {
            seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);seed.Parameters.AddWithValue("@Business",fixture.BusinessId);
            seed.Parameters.AddWithValue("@Customer",customerId);seed.Parameters.AddWithValue("@Site",siteId);
            await seed.ExecuteNonQueryAsync();
        }
        using var diagnosticCommand=new SqlCommand {Connection=connection,CommandTimeout=10};
        try
        {
        foreach(var applications in new[]{false,true})
        foreach(var page in new[]{1,2})
        {
            var query=new ReceivablesReportQuery(page,25,false,new(2026,8,4),customerId,siteId,
                Search:prefix,IncludeApplications:applications,TemplateVersion:4,DateRangeVersion:2);
            using(var capture=new InventoryBalanceProcessingTests.CommandCounter(connection.Database,beforeExecute: command=>
            {
                if(!command.CommandText.Contains("CREATE TABLE #Scoped(ReceivableId",StringComparison.Ordinal))return;
                CaptureReadCommand(command,diagnosticCommand);
            })) await store.ReportAsync(user,query,default);
            var samples=new List<double>();
            for(var i=0;i<20;i++)
            {
                using var reads=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var timer=Stopwatch.StartNew();
                var report=await store.ReportAsync(user,query,default);timer.Stop();
                Assert.Equal(2,reads.Count);Assert.Equal(10001,report.TotalCount);Assert.Equal(10000,report.TotalInvoiceCount);
                Assert.Equal(25,report.Items.Count);Assert.Equal(500_000,report.TotalAvailableAdvances);
                Assert.Equal(500_000,report.TotalAvailableCredits);Assert.Equal(-7500,report.TotalNetAmount);
                Assert.All(report.Items,row=>Assert.Equal(-7500,row.GroupTotals!.NetAmount));
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95=samples.Order().ElementAt(18);
            output.WriteLine($"10,000 positions + 10,000 credits, page {page}, applications={applications}: p95={p95:F1} ms, 2 SQL.");
            if(p95>=1000)
            {
                try {await CaptureReportProfileAsync(diagnosticCommand);}
                catch(Exception diagnostic){output.WriteLine($"Report diagnostics unavailable: {diagnostic.Message}");}
            }
            Assert.True(p95<1000,$"Credit report p95 {p95:F1} ms exceeds 1,000 ms.");
        }
        }
        catch(SqlException error)when(error.Number==-2)
        {
            // Failure-only diagnostics run after the timed reader and its
            // instrumentation have unwound. They never affect a passing budget.
            try {await CaptureReportProfileAsync(diagnosticCommand);}
            catch(Exception diagnostic){output.WriteLine($"Report diagnostics unavailable: {diagnostic.Message}");}
            throw;
        }
    }

    private static void CaptureReadCommand(SqlCommand source,SqlCommand captured)
    {
        captured.CommandText=source.CommandText;
        captured.Parameters.Clear();
        foreach(SqlParameter parameter in source.Parameters)
            captured.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
    }

    private async Task<string?> CaptureReportProfileAsync(SqlCommand captured)
    {
        // Profile the captured read only after all timed samples. Capturing
        // the warmup command in memory avoids relying on an evicted plan cache
        // and keeps diagnostics outside latency and API command-count checks.
        if(string.IsNullOrEmpty(captured.CommandText))return null;
        await using var connection=new SqlConnection(new SqlConnectionStringBuilder(captured.Connection!.ConnectionString)
            {Pooling=false}.ConnectionString);
        await connection.OpenAsync();
        captured.Connection=connection;
        var directory=Path.Combine(AppContext.BaseDirectory,"TestResults","portfolio-report-profile-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // Keep the original cached plan before profiling recompiles a different
        // batch. Its compile time distinguishes a cold optimization cost from
        // the execution work measured by the failure-only profile below.
        await using(var plans=new SqlCommand("""
            SELECT TOP(4) CONVERT(nvarchar(max),planData.query_plan)
            FROM sys.dm_exec_cached_plans cached
            CROSS APPLY sys.dm_exec_sql_text(cached.plan_handle) text
            OUTER APPLY sys.dm_exec_query_plan(cached.plan_handle) planData
            WHERE CHARINDEX(LEFT(@Batch,4000),text.text)>0 AND EXISTS(
              SELECT 1 FROM sys.dm_exec_plan_attributes(cached.plan_handle) attribute
              WHERE attribute.attribute=N'dbid' AND CONVERT(int,attribute.value)=DB_ID())
            ORDER BY cached.usecounts DESC;
            """,connection))
        {
            plans.Parameters.Add("@Batch",System.Data.SqlDbType.NVarChar,-1).Value=captured.CommandText;
            await using var planReader=await plans.ExecuteReaderAsync();
            var planIndex=0;
            while(await planReader.ReadAsync())
                if(!planReader.IsDBNull(0))await File.WriteAllTextAsync(
                    Path.Combine(directory,$"cached-{planIndex++}.sqlplan"),planReader.GetString(0));
        }
        var messages=new List<string>();
        SqlInfoMessageEventHandler onMessage=(_,message)=>messages.Add(message.Message);
        connection.InfoMessage+=onMessage;
        await File.WriteAllTextAsync(Path.Combine(directory,"query.sql"),captured.CommandText);
        captured.CommandText="SET STATISTICS PROFILE ON; SET STATISTICS TIME ON; SET STATISTICS IO ON;\n"+captured.CommandText;
        var index=0;
        try
        {
            await using var reader=await captured.ExecuteReaderAsync();
            do
            {
                var isProfile=Enumerable.Range(0,reader.FieldCount).Any(column=>reader.GetName(column)=="StmtText");
                var rows=new List<Dictionary<string,object?>>();
                while(await reader.ReadAsync())
                    if(isProfile)
                    {
                        var row=new Dictionary<string,object?>();
                        for(var column=0;column<reader.FieldCount;column++)
                            row[reader.GetName(column)]=reader.IsDBNull(column)?null:reader.GetValue(column);
                        rows.Add(row);
                    }
                if(isProfile)await File.WriteAllTextAsync(Path.Combine(directory,$"profile-{index++}.json"),
                    System.Text.Json.JsonSerializer.Serialize(rows));
            }while(await reader.NextResultAsync());
        }
        finally
        {
            connection.InfoMessage-=onMessage;
            await File.WriteAllLinesAsync(Path.Combine(directory,"statistics.txt"),messages);
            output.WriteLine($"Report profiles after timed samples: {index}, {directory}");
        }
        return directory;
    }
}
