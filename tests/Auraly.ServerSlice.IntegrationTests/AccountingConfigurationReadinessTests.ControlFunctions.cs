using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingConfigurationReadinessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_existing_conflicting_control_mapping_is_reported_without_rewriting_its_used_account(bool payable)
    {
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        await store.EnsureDefaultsAsync(User, default);
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            payable ? "2296" : "1396", payable ? "Liability" : "Asset", "General", payable ? "Credit" : "Debit");
        var counterpart = (await store.ListAccountsAsync(User, default)).Single(item => item.Code == "519595");
        var source = Guid.NewGuid();
        using (var posted = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
            new ConfirmManualAccountingVoucherRequest(source, fixture.BusinessId,
                new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Historia de un auxiliar general", [
                    new(account.AccountId, null, null, "Partida general histórica", payable ? 0m : 100m, payable ? 100m : 0m),
                    new(counterpart.AccountId, null, null, "Contrapartida", payable ? 100m : 0m, payable ? 0m : 100m)])))
            Assert.Equal(HttpStatusCode.Accepted, posted.StatusCode);
        var before = (await store.ListAccountsAsync(User, default)).Single(item => item.AccountId == account.AccountId);
        var balanceBefore = await store.GetBalanceSheetAsync(User, new(2026, 10, 21), default);
        var category = payable ? AccountingCategories.AccountsPayable : AccountingCategories.AccountsReceivable;
        var mappingId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        // A synthetic existing configuration bypasses today's mutation guard.
        // The deployment/readiness check must identify it, not rewrite history.
        await using var command = new SqlCommand("""
            DECLARE @Date date=DATEADD(day,1,COALESCE((SELECT MAX(EffectiveFrom)
              FROM dbo.AccountingAccountMappings WHERE TenantId=@Tenant AND BusinessId=@Business
                AND Category=@Category),CONVERT(date,'20260101')));
            INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,CreatedAt)
              VALUES(@Mapping,@Tenant,@Business,@Category,@Account,@Date,SYSDATETIMEOFFSET());
            SELECT @Date;
            """, connection);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Category", category);
        command.Parameters.AddWithValue("@Mapping", mappingId);
        command.Parameters.AddWithValue("@Account", account.AccountId);
        try
        {
            var date = DateOnly.FromDateTime((DateTime)(await command.ExecuteScalarAsync())!);
            var readiness = await store.GetReadinessAsync(User, date, null, default);
            Assert.Contains(readiness.BlockingIssues, issue => issue.Contains(category, StringComparison.Ordinal));
            await store.EnsureDefaultsAsync(User, default);
            Assert.Equal(before, (await store.ListAccountsAsync(User, default)).Single(item => item.AccountId == account.AccountId));
            Assert.Equal(balanceBefore, await store.GetBalanceSheetAsync(User, new(2026, 10, 21), default));
            command.CommandText = """
                SELECT AccountId FROM dbo.AccountingAccountMappings WHERE MappingId=@Mapping;
                SELECT (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE BusinessId=@Business AND SourceDocumentId=@Source),
                  (SELECT COUNT(*) FROM dbo.Payables WHERE BusinessId=@Business AND SourceDocumentId=@Source),
                  (SELECT COUNT(*) FROM dbo.Receivables WHERE BusinessId=@Business AND SourceDocumentId=@Source);
                """;
            command.Parameters.AddWithValue("@Source", source);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(account.AccountId, reader.GetGuid(0));
            await reader.NextResultAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal(0, reader.GetInt32(2));
        }
        finally
        {
            command.CommandText = "DELETE dbo.AccountingAccountMappings WHERE MappingId=@Mapping AND TenantId=@Tenant";
            await command.ExecuteNonQueryAsync();
        }
    }
}
