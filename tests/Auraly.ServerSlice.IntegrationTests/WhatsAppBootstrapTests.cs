using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class WhatsAppBootstrapTests(ServerSliceFixture fixture)
{
    private const string CjBusiness = "C1D15A00-0000-0000-0000-000000000010";
    private const string CjNumber = "C1D15A00-0000-0000-0000-000000000050";
    private const string LuisBusiness = "BABA0000-0000-0000-0000-000000000001";
    private const string OtherBusiness = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task Publishing_again_preserves_managed_channel_owners_credentials_and_activation()
    {
        await using var connection = await OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ClearChannelsAsync(connection, transaction);
        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.Businesses SET IsActive=1 WHERE BusinessId=@Luis;
            UPDATE dbo.Agents SET IsActive=1 WHERE BusinessId=@Luis;
            INSERT dbo.BusinessWhatsAppNumbers
              (BusinessWhatsAppNumberId,BusinessId,PhoneNumber,WhatsAppPhoneNumberId,WhatsAppAccessToken,IsActive)
            VALUES(NEWID(),@Other,N'570000000001',N'1234810033044432',N'test-managed-token',1),
              (NEWID(),@Luis,N'570000000002',N'1256148054246934',N'test-other-token',1);
            """, new("@Luis", Guid.Parse(LuisBusiness)), new("@Other", Guid.Parse(OtherBusiness)));
        var before = await SnapshotAsync(connection, transaction);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await RunDeploymentChannelsAsync(connection, transaction);
            Assert.Equal(before, await SnapshotAsync(connection, transaction));
        }
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Initial_provisioning_is_inactive_without_a_token_and_never_overwrites_later_configuration()
    {
        await using var connection = await OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ClearChannelsAsync(connection, transaction);
        await RunDeploymentChannelsAsync(connection, transaction);
        await using (var state = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.BusinessWhatsAppNumbers
            WHERE BusinessWhatsAppNumberId=@Id AND BusinessId=@Business AND IsActive=0 AND WhatsAppAccessToken=N'';
            """, connection, transaction))
        {
            state.Parameters.AddWithValue("@Id", Guid.Parse(CjNumber));
            state.Parameters.AddWithValue("@Business", Guid.Parse(CjBusiness));
            Assert.Equal(1, Convert.ToInt32(await state.ExecuteScalarAsync()));
        }
        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.BusinessWhatsAppNumbers SET PhoneNumber=N'570000000099',
              WhatsAppBusinessAccountId=N'edited-account',WhatsAppAccessToken=N'test-admin-token',IsActive=0
            WHERE BusinessWhatsAppNumberId=@Id;
            """, new SqlParameter("@Id", Guid.Parse(CjNumber)));
        var configured = await SnapshotAsync(connection, transaction);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await RunDeploymentChannelsAsync(connection, transaction, "test-new-deployment-token");
            Assert.Equal(configured, await SnapshotAsync(connection, transaction));
        }

        // An administrator may also change the Meta ID and owning business.
        // The stable row ID must still prevent recreating the old default.
        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.BusinessWhatsAppNumbers SET BusinessId=@Other,AgentId=NULL,
              WhatsAppPhoneNumberId=N'edited-meta-id' WHERE BusinessWhatsAppNumberId=@Id;
            """, new("@Other", Guid.Parse(OtherBusiness)), new("@Id", Guid.Parse(CjNumber)));
        var reassigned = await SnapshotAsync(connection, transaction);
        await RunDeploymentChannelsAsync(connection, transaction);
        Assert.Equal(reassigned, await SnapshotAsync(connection, transaction));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Initial_provisioning_with_a_token_creates_one_channel_and_warehouse_binding()
    {
        await using var connection = await OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ClearChannelsAsync(connection, transaction);
        await RunDeploymentChannelsAsync(connection, transaction, "test-initial-token");
        await using (var state = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.BusinessWhatsAppNumbers n
            JOIN dbo.IntegrationChannelWarehouses w ON w.BusinessWhatsAppNumberId=n.BusinessWhatsAppNumberId
            WHERE n.BusinessWhatsAppNumberId=@Id AND n.BusinessId=@Business AND n.IsActive=1
              AND n.WhatsAppAccessToken=N'test-initial-token' AND w.BusinessId=@Business
              AND w.IntegrationConnectionId='C1D15A00-0000-0000-0000-000000000030'
              AND w.WarehouseCode=N'2' AND w.IsActive=1;
            """, connection, transaction))
        {
            state.Parameters.AddWithValue("@Id", Guid.Parse(CjNumber));
            state.Parameters.AddWithValue("@Business", Guid.Parse(CjBusiness));
            Assert.Equal(1, Convert.ToInt32(await state.ExecuteScalarAsync()));
        }
        var before = await SnapshotAsync(connection, transaction);
        await RunDeploymentChannelsAsync(connection, transaction, "test-replacement-token");
        Assert.Equal(before, await SnapshotAsync(connection, transaction));
        await transaction.RollbackAsync();
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        return connection;
    }

    private static Task ClearChannelsAsync(SqlConnection connection, SqlTransaction transaction) =>
        ExecuteAsync(connection, transaction, """
            DELETE dbo.IntegrationChannelWarehouses;
            DELETE dbo.BusinessWhatsAppNumbers;
            """);

    private static async Task RunDeploymentChannelsAsync(SqlConnection connection, SqlTransaction transaction,
        string token = "")
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Auraly.Commerce.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var scripts = Path.Combine(root.FullName, "database", "Auraly.Database", "Scripts");
        var postDeployment = await File.ReadAllTextAsync(Path.Combine(scripts, "PostDeployment.sql"));
        var channelFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MigrateLuisWhatsAppToCJ.sql", "MigrateCJWhatsAppToMedidental.sql",
            "MigrateMedidentalWhatsAppToDigitalShop.sql", "MigrateDigitalShopWhatsAppToCJ.sql", "SeedCJWhatsAppNumber.sql"
        };
        var paths = Regex.Matches(postDeployment, @"(?m)^:r\s+\.\\(?<path>[^\r\n]+)")
            .Select(match => match.Groups["path"].Value.Trim().Replace('\\', Path.DirectorySeparatorChar))
            .Where(path => channelFiles.Contains(Path.GetFileName(path))).ToArray();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            var sql = (await File.ReadAllTextAsync(Path.Combine(scripts, path)))
                .Replace("$(DeploymentEnvironment)", "dev", StringComparison.Ordinal)
                .Replace("$(CJWhatsAppAccessToken)", token.Replace("'", "''"), StringComparison.Ordinal);
            await ExecuteAsync(connection, transaction, sql);
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> SnapshotAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = new SqlCommand("""
            SELECT
              CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM dbo.BusinessWhatsAppNumbers
                ORDER BY BusinessWhatsAppNumberId FOR JSON PATH,INCLUDE_NULL_VALUES)),2) Channels,
              CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM dbo.IntegrationChannelWarehouses
                ORDER BY IntegrationChannelWarehouseId FOR JSON PATH,INCLUDE_NULL_VALUES)),2) Bindings,
              CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT BusinessId,IsActive FROM dbo.Businesses
                WHERE BusinessId=@Luis FOR JSON PATH)),2) SourceBusiness,
              CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT AgentId,IsActive FROM dbo.Agents
                WHERE BusinessId=@Luis ORDER BY AgentId FOR JSON PATH)),2) SourceAgents
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Luis", Guid.Parse(LuisBusiness));
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
