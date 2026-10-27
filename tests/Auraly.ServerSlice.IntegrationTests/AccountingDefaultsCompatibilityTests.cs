using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Fact]
    public async Task Reprovisioning_preserves_the_users_existing_mapping_across_years()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var accountId = Guid.NewGuid();
            var mappingId = Guid.NewGuid();
            await using var command = new SqlCommand("""
                INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
                VALUES(@AccountId,@TenantId,@Code,N'Cuenta elegida por el contador',N'Expense',1,0,1,SYSDATETIMEOFFSET());
                DELETE dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND BusinessId IS NULL AND Category=N'OperatingExpense';
                INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
                VALUES(@MappingId,@TenantId,NULL,N'OperatingExpense',@AccountId,'20000101',NULL,SYSDATETIMEOFFSET());
                EXEC dbo.AccountingDefaultsProvision @TenantId,@BusinessId,'2026-10-23T12:00:00-05:00';
                EXEC dbo.AccountingDefaultsProvision @TenantId,@BusinessId,'2027-10-23T12:00:00-05:00';
                SELECT MappingId,AccountId,EffectiveFrom,EffectiveTo FROM dbo.AccountingAccountMappings
                WHERE TenantId=@TenantId AND BusinessId IS NULL AND Category=N'OperatingExpense'
                ORDER BY EffectiveFrom;
                """, connection, transaction);
            command.Parameters.AddWithValue("@AccountId", accountId); command.Parameters.AddWithValue("@MappingId", mappingId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId); command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@Code", "5199" + BitConverter.ToUInt64(accountId.ToByteArray()).ToString("D20"));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.Equal(mappingId, reader.GetGuid(0)); Assert.Equal(accountId, reader.GetGuid(1));
            Assert.Equal(new DateTime(2000, 1, 1), reader.GetDateTime(2)); Assert.True(reader.IsDBNull(3));
            Assert.False(await reader.ReadAsync());
        }
        finally { await transaction.RollbackAsync(); }
    }
}
