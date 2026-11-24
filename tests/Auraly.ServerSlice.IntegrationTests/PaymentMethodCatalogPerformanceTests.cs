using System.Diagnostics;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaymentMethodCatalogPerformanceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Payment catalog performance";
}

[Collection(PaymentMethodCatalogPerformanceCollection.Name)]
public sealed class PaymentMethodCatalogPerformanceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Catalog_pages_share_read_only_readiness_without_blocking_on_a_posting_account_lock()
    {
        var accountId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = new SqlCommand("""
            INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,
              IsActive,CreatedAt,FinancialFunction,BalanceSide)
              VALUES(@Account,@Tenant,N'PERF-CAJA',N'Caja de rendimiento',N'Asset',1,0,1,SYSUTCDATETIME(),N'Cash',N'Debit');
            WITH Digits AS(SELECT n FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n))
            INSERT reference.PaymentMethods(TenantId,PaymentMethodId,Code,Name,PaymentTypeId,AccountId,
              UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,UseForVouchers,IsActive,SortOrder,Version,CreatedAt,UpdatedAt)
            SELECT @Tenant,NEWID(),CONCAT(N'PERF-FORMA-',RIGHT(N'000'+CONVERT(nvarchar(4),a.n+10*b.n+100*c.n),3)),
              CONCAT(N'Forma de prueba ',a.n+10*b.n+100*c.n),type.OptionId,@Account,1,1,1,1,1,1,10,1,SYSUTCDATETIME(),SYSUTCDATETIME()
            FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c
            CROSS JOIN reference.Options type WHERE type.CatalogCode=N'payment-method' AND type.Code=N'Cash'
              AND a.n+10*b.n+100*c.n<500;
            """, connection))
        {
            seed.Parameters.AddWithValue("@Account", accountId);
            seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            await seed.ExecuteNonQueryAsync();
        }
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPaymentMethodStore>();
        var actor = new CatalogUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId, new HashSet<string>());
        foreach (var usage in new[] { "Sales", "OrderReceipts", "Receipts", "SupplierPayments", "Payroll", "VoucherReceipts", "VoucherPayments" })
        foreach (var (search, page) in new[] { ("PERF-FORMA", 1), ("PERF-FORMA", 2), ("PERF-FORMA-499", 1), ("PERF-FORMA-MISSING", 1) })
        {
            var samples = new List<double>();
            for (var sample = 0; sample < 6; sample++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    commandFilter: command => command.CommandText.Contains("#EligiblePaymentForms", StringComparison.Ordinal));
                var timer = Stopwatch.StartNew();
                var result = await store.ListAsync(actor, new(Page: page, PageSize: 10, Search: search, Usage: usage, MethodCode: "Cash"), default);
                timer.Stop();
                Assert.Equal(1, counter.Count);
                if (search == "PERF-FORMA") { Assert.Equal(500, result.TotalCount); Assert.Equal(10, result.Items.Count); }
                else if (search == "PERF-FORMA-499") Assert.Equal("PERF-FORMA-499", Assert.Single(result.Items).Code);
                else { Assert.Equal(0, result.TotalCount); Assert.Empty(result.Items); }
                if (sample > 0) samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var worst = samples.Max();
            output.WriteLine("{0}, {1}, page {2}: max={3:F1}ms, one command, five warm samples", usage, search, page, worst);
            Assert.True(worst < 500, $"Payment catalog exceeded 500ms: {worst:F1}ms.");
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using (var hold = new SqlCommand("""
            SELECT AccountId FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK,INDEX(PK_AccountingAccounts))
            WHERE TenantId=@Tenant AND AccountId=@Account;
            """, connection, transaction))
        {
            hold.Parameters.AddWithValue("@Tenant", fixture.TenantId); hold.Parameters.AddWithValue("@Account", accountId);
            Assert.Equal(accountId, await hold.ExecuteScalarAsync());
        }
        try
        {
            // An ordinary catalog read may share the financial writer's update lock.
            // The old locking predicate timed out here even without any account edit.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var readable = await store.ListAsync(actor, new(PageSize: 10, Search: "PERF-FORMA-499", Usage: "Sales", MethodCode: "Cash"), timeout.Token);
            Assert.Single(readable.Items);
            await using var contender = new SqlConnection(fixture.ConnectionString);
            await contender.OpenAsync();
            await using var posting = new SqlCommand("""
                SELECT ready.AccountId FROM accounting.PaymentPostingCategory(N'PosPaymentMethod',N'Cash') category
                CROSS APPLY accounting.PostingAccountReadiness(@Tenant,category.Category,@Account) ready;
                """, contender) { CommandTimeout = 1 };
            posting.Parameters.AddWithValue("@Tenant", fixture.TenantId); posting.Parameters.AddWithValue("@Account", accountId);
            var blocked = await Assert.ThrowsAsync<SqlException>(async () => { await posting.ExecuteScalarAsync(); });
            Assert.Equal(-2, blocked.Number);
        }
        finally { await transaction.RollbackAsync(); }
        await using var untouched = new SqlCommand("SELECT ConfigurationLockedAt FROM dbo.AccountingAccounts WHERE AccountId=@Account", connection);
        untouched.Parameters.AddWithValue("@Account", accountId);
        Assert.IsType<DBNull>(await untouched.ExecuteScalarAsync());
    }
}
