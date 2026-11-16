using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PricingRetirementUpgradeTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData("Absent")]
    [InlineData("Reviewed")]
    [InlineData("Customized")]
    [InlineData("Dependent")]
    public async Task Retiring_the_obsolete_price_resolver_is_exact_idempotent_and_data_preserving(string state)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        async Task Execute(string sql)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        async Task<byte[]> Prices()
        {
            await using var command = new SqlCommand("""
                SELECT HASHBYTES('SHA2_256',(SELECT * FROM dbo.ProductPrices
                  ORDER BY ProductPriceId FOR JSON PATH));
                """, connection, transaction);
            return (byte[])(await command.ExecuteScalarAsync())!;
        }
        try
        {
            var before = await Prices();
            if (state is "Reviewed" or "Dependent")
            {
                // Recreate only the schema required to compile the historical
                // fixture. All changes are rolled back in this disposable DB.
                await Execute("""
                    ALTER TABLE dbo.ProductCategories ADD BusinessId uniqueidentifier NULL;
                    ALTER TABLE dbo.PriceChannels ADD BusinessId uniqueidentifier NULL;
                    ALTER TABLE dbo.Customers ADD BusinessId uniqueidentifier NULL;
                    CREATE TABLE dbo.ResolvedPriceChannelItems(
                      ResolvedPriceChannelItemId uniqueidentifier,PriceChannelId uniqueidentifier,
                      ProductId uniqueidentifier,MinimumQuantity decimal(19,6),
                      Amount decimal(19,4),IsActive bit,CreatedAt datetimeoffset(7));
                    """);
                await Execute("""
                    CREATE FUNCTION dbo.PriceChannelAmountCalculate(@Strategy nvarchar(48),
                      @Value decimal(19,6),@Base decimal(19,4),@Cost decimal(19,6),
                      @Latest decimal(19,6),@Margin decimal(9,6),@Special decimal(19,4))
                    RETURNS decimal(19,4) AS BEGIN RETURN @Base; END;
                    """);
                await Execute(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
                    "Fixtures", "CustomerProductPriceResolve.retired.sql")));
                if (state == "Dependent")
                    await Execute("""
                        CREATE VIEW dbo.QaRetiredPriceConsumer AS
                        SELECT Amount FROM dbo.CustomerProductPriceResolve(NULL,NULL,NULL,NULL,1,SYSDATETIMEOFFSET());
                        """);
            }
            else if (state == "Customized")
                await Execute("CREATE FUNCTION dbo.CustomerProductPriceResolve() RETURNS TABLE AS RETURN(SELECT 1 Amount);");

            if (state is "Customized" or "Dependent")
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => fixture.ApplyMigrationAsync(
                    "20261116_RetireObsoleteCustomerPriceResolver.sql", connection, transaction));
                Assert.Equal(state == "Customized" ? 51980 : 51981, error.Number);
            }
            else
            {
                await fixture.ApplyMigrationAsync("20261116_RetireObsoleteCustomerPriceResolver.sql", connection, transaction);
                await fixture.ApplyMigrationAsync("20261116_RetireObsoleteCustomerPriceResolver.sql", connection, transaction);
                Assert.Equal(before, await Prices());
                await using var verify = new SqlCommand("SELECT OBJECT_ID(N'dbo.CustomerProductPriceResolve');",connection,transaction);
                Assert.Equal(DBNull.Value,await verify.ExecuteScalarAsync());
            }
        }
        finally { if (transaction.Connection is not null) await transaction.RollbackAsync(); }
    }
}
