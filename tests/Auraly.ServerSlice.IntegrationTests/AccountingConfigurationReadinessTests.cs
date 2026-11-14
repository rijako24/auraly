using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed partial class AccountingConfigurationReadinessTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    private AccountingUserIdentity User => new(fixture.UserId,fixture.TenantId,fixture.BusinessId,
        new HashSet<string> { AccountingPermissionCodes.Configure,AccountingPermissionCodes.Read,AccountingPermissionCodes.Activate });

    [Fact]
    public async Task Fresh_defaults_validate_required_categories_instead_of_equating_account_and_mapping_counts()
    {
        using var scope=fixture.CreateScope();
        var store=scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var database=new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        using (var counter=new InventoryBalanceProcessingTests.CommandCounter(database))
        {
            var defaults=await store.EnsureDefaultsAsync(User,default);
            Assert.True(defaults.IsReady); Assert.True(defaults.HasDefaultCostCenter); Assert.True(defaults.HasOpenPeriod);
            Assert.Equal(1,counter.Count);
        }
        var accounts=await store.ListAccountsAsync(User,default);
        Assert.Equal("Liability",Assert.Single(accounts,account=>account.Code=="240810").AccountType);
        var readiness=await store.GetReadinessAsync(User,new(2026,8,3),null,default);
        Assert.Empty(readiness.BlockingIssues);
    }

    [Fact]
    public async Task A_used_legacy_input_vat_account_is_rejected_without_reclassifying_history_or_reactivating_accounting()
    {
        using var scope=fixture.CreateScope();
        var store=scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var accounts=await store.ListAccountsAsync(User,default);
        var input=Assert.Single(accounts,account=>account.Code=="240810");
        var counterpart=Assert.Single(accounts,account=>account.Code=="519595");
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using var update=new SqlCommand("UPDATE dbo.AccountingAccounts SET AccountType=@Type WHERE TenantId=@Tenant AND AccountId=@Account",connection);
        update.Parameters.AddWithValue("@Type","Asset"); update.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        update.Parameters.AddWithValue("@Account",input.AccountId);
        Assert.Equal(1,await update.ExecuteNonQueryAsync());
        try
        {
            using var client=fixture.CreateAdminClient(AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend);
            var source=Guid.NewGuid();
            using (var posted=await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
                new ConfirmManualAccountingVoucherRequest(source,fixture.BusinessId,new(2026,8,2,12,0,0,TimeSpan.Zero),
                    "MANUAL_VOUCHER","Historia de la configuración anterior",[
                        new(input.AccountId,null,null,"IVA histórico",19m,0m),
                        new(counterpart.AccountId,null,null,"Contrapartida de prueba",0m,19m)])))
                Assert.True(posted.StatusCode==HttpStatusCode.Accepted,await posted.Content.ReadAsStringAsync());
            var before=await store.GetBalanceSheetAsync(User,new(2026,8,2),default);
            Assert.Contains(before,row=>row.AccountCode==input.Code && row.Section=="Asset");
            var defaults=await store.EnsureDefaultsAsync(User,default);
            Assert.False(defaults.IsReady);
            // Counts remain sufficient: eligibility, rather than more rows, is missing.
            Assert.True(defaults.MappingCount>0); Assert.True(defaults.AccountCount>defaults.MappingCount);
            var readiness=await store.GetReadinessAsync(User,new(2026,8,3),null,default);
            Assert.Contains(readiness.BlockingIssues,issue=>issue.Contains("InputVat",StringComparison.Ordinal));
            Assert.Equal("Asset",Assert.Single(await store.ListAccountsAsync(User,default),account=>account.AccountId==input.AccountId).AccountType);
            Assert.Equal(before,await store.GetBalanceSheetAsync(User,new(2026,8,2),default));
            var replay=await store.ActivateAsync(User,new(new(2026,1,1),"COP","ZeroDeclared"),default);
            Assert.Equal(AccountingActivationStatuses.Ready,replay.Status);
            await using var postedOnce=new SqlCommand("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Source AND BusinessId=@Business",connection);
            postedOnce.Parameters.AddWithValue("@Source",source); postedOnce.Parameters.AddWithValue("@Business",fixture.BusinessId);
            Assert.Equal(1,Convert.ToInt32(await postedOnce.ExecuteScalarAsync()));
        }
        finally
        {
            update.Parameters["@Type"].Value=input.AccountType;
            Assert.Equal(1,await update.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task Invalid_site_override_is_not_hidden_by_the_valid_shared_mapping()
    {
        using var scope=fixture.CreateScope(); var store=scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var accounts=await store.ListAccountsAsync(User,default);
        var wrong=Assert.Single(accounts,account=>account.Code=="110505"); var mapping=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using var command=new SqlCommand("""
            INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,CreatedAt)
            VALUES(@Mapping,@Tenant,@Business,N'InputVat',@Account,'20000101',SYSUTCDATETIME());
            """,connection);
        command.Parameters.AddWithValue("@Mapping",mapping); command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        command.Parameters.AddWithValue("@Business",fixture.BusinessId); command.Parameters.AddWithValue("@Account",wrong.AccountId);
        await command.ExecuteNonQueryAsync();
        try
        {
            Assert.False((await store.EnsureDefaultsAsync(User,default)).IsReady);
            Assert.Contains((await store.GetReadinessAsync(User,new(2026,8,3),null,default)).BlockingIssues,
                issue=>issue.Contains("InputVat",StringComparison.Ordinal));
            command.CommandText="SELECT COUNT(*) FROM accounting.ConfiguredPostingAccount(@Tenant,@Business,N'InputVat','20260803');";
            Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        finally
        {
            command.CommandText="DELETE dbo.AccountingAccountMappings WHERE MappingId=@Mapping AND TenantId=@Tenant";
            Assert.Equal(1,await command.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    [Trait("Performance","AccountingReadiness")]
    public async Task Configuration_diagnostics_do_not_take_first_use_update_locks_and_keep_three_sql_commands()
    {
        using var scope=fixture.CreateScope(); var store=scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        await store.GetReadinessAsync(User,new(2026,8,3),null,default);
        await using var connection=new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync();
        await using(var locked=new SqlCommand("SELECT AccountId FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@Tenant AND Code=N'240810'",connection,transaction))
        {
            locked.Parameters.AddWithValue("@Tenant",fixture.TenantId); Assert.IsType<Guid>(await locked.ExecuteScalarAsync());
        }
        var samples=new List<double>();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for(var sample=0;sample<20;sample++)
        {
            using var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
            var timer=Stopwatch.StartNew();
            var readiness=await store.GetReadinessAsync(User,new(2026,8,3),null,timeout.Token);
            timer.Stop(); Assert.Empty(readiness.BlockingIssues); Assert.Equal(3,counter.Count);
            samples.Add(timer.Elapsed.TotalMilliseconds);
        }
        await transaction.RollbackAsync();
        var p95=samples.Order().ElementAt(18);
        output.WriteLine($"Readiness: 20 muestras, 3 SQL por consulta, p95={p95:F1}ms; cuenta con bloqueo U concurrente.");
        Assert.True(p95<1000,$"Readiness p95 {p95:F1}ms exceeds 1000ms.");
    }
}
