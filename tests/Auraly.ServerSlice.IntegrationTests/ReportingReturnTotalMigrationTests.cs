using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ReportingReturnTotalUpgradeCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Reporting return total upgrade";
}

[Collection(ReportingReturnTotalUpgradeCollection.Name)]
public sealed class ReportingReturnTotalMigrationTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Upgrade_preserves_confirmed_returns_and_money_rejects_unverified_facts_and_counts_tax_groups_once()
    {
        // Reuse the existing end-to-end dispatch scenario and its assertions;
        // the migration has a disposable database, separate from other suites.
        var scenario = new AccountingVerticalSliceTests(fixture, output);
        await scenario.InitializeAsync();
        try
        {
            await scenario.Order_dispatch_collection_uses_one_session_and_reclassifies_only_changed_accounts(
                "SalesReceipt", "Cash", true, false, "Partial", verifyReporting: true);
        }
        finally { await scenario.DisposeAsync(); }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var unknownHistory = Guid.NewGuid();
        var knownHistory = Guid.NewGuid();
        var otherBusiness = Guid.NewGuid();
        var returned = (Guid)(await ScalarAsync("SELECT TOP(1) ReturnId FROM dbo.SalesReturns WHERE BusinessId=@Business"))!;
        var official = (decimal)(await ScalarAsync("SELECT TotalAmount FROM dbo.SalesReturns WHERE ReturnId=@Return", returned))!;
        await ExecuteAsync("""
            UPDATE reporting.SalesReportTaxFacts SET TaxableAmount=TaxableAmount/2,TaxAmount=TaxAmount/2,TotalAmount=TotalAmount/2
              WHERE SourceDocumentId=@Return AND SourceDocumentType=N'SalesReturn';
            INSERT reporting.SalesReportTaxFacts(OccurredAt,SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,
              TaxCode,TaxRate,TenantId,BusinessId,BusinessLocalDate,BusinessLocalHour,TaxableAmount,TaxAmount,
              TotalAmount,ProjectedAt,OfficialDocumentTotalAmount)
            SELECT OccurredAt,SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,N'TEST-SECOND',TaxRate,
              TenantId,BusinessId,BusinessLocalDate,BusinessLocalHour,TaxableAmount,TaxAmount,TotalAmount,ProjectedAt,OfficialDocumentTotalAmount
            FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Return AND SourceDocumentType=N'SalesReturn';
            INSERT reporting.SalesReportTaxFacts(OccurredAt,SourceDocumentId,SourceDocumentType,
              TaxCode,TaxRate,TenantId,BusinessId,BusinessLocalDate,BusinessLocalHour,TaxableAmount,TaxAmount,
              TotalAmount,ProjectedAt)
            SELECT fact.OccurredAt,history.Id,fact.SourceDocumentType,fact.TaxCode,fact.TaxRate,
              fact.TenantId,fact.BusinessId,fact.BusinessLocalDate,fact.BusinessLocalHour,fact.TaxableAmount,
              fact.TaxAmount,fact.TotalAmount,fact.ProjectedAt
            FROM reporting.SalesReportTaxFacts fact CROSS JOIN (VALUES(@Unknown),(@Known)) history(Id)
            WHERE fact.SourceDocumentId=@Return;
            INSERT reporting.SalesReportPaymentFacts(SourceDocumentId,SourceDocumentType,PaymentNumber,
              TenantId,BusinessId,BusinessLocalDate,MovementType,MethodCode,Amount,ProjectedAt)
            VALUES(@Known,N'SalesReturn',1,@Tenant,@Business,'20261120',N'Refund',N'Cash',-42.25,SYSUTCDATETIME());
            """, returned);
        var before = await SnapshotAsync();
        await ExecuteAsync("""
            ALTER TABLE reporting.SalesReportTaxFacts DROP CONSTRAINT CK_SalesReportTaxFacts_ReturnTotal;
            DROP INDEX IX_SalesReportTaxFacts_Business_Date_Tax ON reporting.SalesReportTaxFacts;
            ALTER TABLE reporting.SalesReportTaxFacts DROP COLUMN OfficialDocumentTotalAmount,OriginalSaleDocumentId;
            """, returned);

        await ApplyAsync();
        await AssertPreservedAsync();
        await ApplyAsync();
        await AssertPreservedAsync();

        // Existing non-null metadata is evidence, not an amount to overwrite.
        await ExecuteAsync("UPDATE reporting.SalesReportTaxFacts SET OfficialDocumentTotalAmount=-1 WHERE SourceDocumentId=@Return", returned);
        var conflicting = await Assert.ThrowsAsync<SqlException>(ApplyAsync);
        Assert.Equal(51625, conflicting.Number);
        Assert.Equal(-1m, (decimal)(await ScalarAsync("SELECT MAX(OfficialDocumentTotalAmount) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Return", returned))!);
        await ExecuteAsync("UPDATE fact SET OfficialDocumentTotalAmount=-r.TotalAmount FROM reporting.SalesReportTaxFacts fact JOIN dbo.SalesReturns r ON r.ReturnId=fact.SourceDocumentId WHERE fact.SourceDocumentId=@Return", returned);

        await ExecuteAsync("UPDATE reporting.SalesReportTaxFacts SET OriginalSaleDocumentId=NEWID() WHERE SourceDocumentId=@Return", returned);
        var unverified = await Assert.ThrowsAsync<SqlException>(ApplyAsync);
        Assert.Equal(51624, unverified.Number);
        await ExecuteAsync("UPDATE fact SET OriginalSaleDocumentId=r.OriginalDocumentId FROM reporting.SalesReportTaxFacts fact JOIN dbo.SalesReturns r ON r.ReturnId=fact.SourceDocumentId WHERE fact.SourceDocumentId=@Return", returned);
        await ExecuteAsync("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
            VALUES(@OtherBusiness,@Tenant,N'Other test business',N'',N'',N'',N'',N'');
            UPDATE reporting.SalesReportTaxFacts SET BusinessId=@OtherBusiness WHERE SourceDocumentId=@Return;
            """, returned);
        var wrongBusiness = await Assert.ThrowsAsync<SqlException>(ApplyAsync);
        Assert.Equal(51624, wrongBusiness.Number);
        await ExecuteAsync("UPDATE reporting.SalesReportTaxFacts SET BusinessId=@Business WHERE SourceDocumentId=@Return", returned);
        await ApplyAsync();
        await AssertPreservedAsync();
        await fixture.ReapplySchemaAsync();
        await AssertPreservedAsync();

        Task ApplyAsync() => fixture.ApplyMigrationAsync("20261121_ProjectReturnOfficialTotals.sql", connection);

        async Task AssertPreservedAsync()
        {
            Assert.Equal(before, await SnapshotAsync());
            Assert.Equal(2, (int)(await ScalarAsync("SELECT COUNT(*) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Return AND OfficialDocumentTotalAmount=-@Amount", returned, official))!);
            Assert.Equal(-official, (decimal)(await ScalarAsync("SELECT SUM(OfficialTotal) FROM reporting.SalesReportDocumentMovements(@Tenant,@Business,'20261101','20261231') WHERE SourceDocumentId=@Return", returned))!);
            Assert.Equal(0, (int)(await ScalarAsync("SELECT COUNT(*) FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Return", returned))!);
            Assert.Equal(2, (int)(await ScalarAsync("SELECT COUNT(*) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Unknown AND OfficialDocumentTotalAmount IS NULL AND OriginalSaleDocumentId IS NULL"))!);
            Assert.Equal(2, (int)(await ScalarAsync("SELECT COUNT(*) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Known AND OfficialDocumentTotalAmount=-42.25 AND OriginalSaleDocumentId IS NULL"))!);
            Assert.Equal(0, (int)(await ScalarAsync("SELECT COUNT(*) FROM reporting.SalesReportDocumentMovements(@Tenant,@Business,'20261101','20261231') WHERE SourceDocumentId=@Unknown"))!);
            Assert.Equal(-42.25m, (decimal)(await ScalarAsync("SELECT SUM(OfficialTotal) FROM reporting.SalesReportDocumentMovements(@Tenant,@Business,'20261101','20261231') WHERE SourceDocumentId=@Known"))!);
        }

        async Task<string> SnapshotAsync() => (string)(await ScalarAsync("""
            SELECT (SELECT OBJECT_ID(N'reporting.SalesReportTaxFacts') TableId,
              (SELECT * FROM dbo.SalesReturns WHERE ReturnId=@Return FOR JSON PATH) Confirmed,
              (SELECT SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,TaxCode,TaxRate,TenantId,BusinessId,
                BusinessLocalDate,TaxableAmount,TaxAmount,TotalAmount,ProjectedAt,OccurredAt,BusinessLocalHour
               FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId IN(@Return,@Unknown,@Known) ORDER BY SourceDocumentId,TaxCode,TaxRate FOR JSON PATH) Projection,
              (SELECT SourceDocumentId,SourceDocumentType,PayloadHash,AccountingEntryRequired FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Return ORDER BY SourceDocumentType FOR JSON PATH) Sources,
              (SELECT * FROM dbo.AccountingEntries WHERE SourceDocumentId=@Return ORDER BY EntryId FOR JSON PATH) Entries,
              (SELECT * FROM dbo.InventoryMovements WHERE DocumentId=@Return ORDER BY InventoryMovementId FOR JSON PATH) Inventory,
              (SELECT * FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId IN(@Return,@Unknown,@Known) ORDER BY SourceDocumentId,PaymentNumber FOR JSON PATH) Money
              FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
            """, returned))!;

        async Task<object?> ScalarAsync(string sql, Guid? id = null, decimal? amount = null)
        {
            await using var command = Command(sql, id, amount);
            return await command.ExecuteScalarAsync();
        }
        async Task ExecuteAsync(string sql, Guid id)
        {
            await using var command = Command(sql, id);
            await command.ExecuteNonQueryAsync();
        }
        SqlCommand Command(string sql, Guid? id, decimal? amount = null)
        {
            var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            command.Parameters.AddWithValue("@Business", fixture.BusinessId);
            command.Parameters.AddWithValue("@Return", (object?)id ?? DBNull.Value);
            command.Parameters.AddWithValue("@Unknown", unknownHistory);
            command.Parameters.AddWithValue("@Known", knownHistory);
            command.Parameters.AddWithValue("@OtherBusiness", otherBusiness);
            if (amount is { } value) command.Parameters.AddWithValue("@Amount", value);
            return command;
        }
    }
}
