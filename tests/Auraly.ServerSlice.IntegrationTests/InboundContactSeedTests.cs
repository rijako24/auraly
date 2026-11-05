using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class InboundContactSeedTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Rerunning_the_seed_preserves_edited_contacts_and_existing_phone_identities()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Auraly.Commerce.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var seed = await File.ReadAllTextAsync(Path.Combine(root.FullName, "database", "Auraly.Database",
            "Scripts", "Seeds", "SeedSystemAgentTemplatesAndInboundContacts.sql"));
        var contactId = Guid.Parse("BABA0000-0000-0000-0000-0000000000B1");
        var businessId = Guid.Parse("BABA0000-0000-0000-0000-000000000001");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        // Exercise fresh provisioning, then the edited/inactive identity that
        // previously made the next deployment fail with duplicate primary key.
        await ExecuteAsync("DELETE dbo.BusinessInboundContacts WHERE BusinessInboundContactId=@Contact;",
            new SqlParameter("@Contact", contactId));
        await ExecuteAsync(seed);
        var fresh = await SnapshotAsync();
        using var original = JsonDocument.Parse(fresh);
        var row = Assert.Single(original.RootElement.EnumerateArray());
        Assert.Equal(contactId, row.GetProperty("BusinessInboundContactId").GetGuid());
        var phone = row.GetProperty("PhoneNumber").GetString();
        var normalized = row.GetProperty("PhoneNormalized").GetString();
        await ExecuteAsync("""
            UPDATE dbo.BusinessInboundContacts SET PhoneNumber=N'+571000000099',PhoneNormalized=N'571000000099',
              Name=N'Contacto personalizado',IsActive=0,CapabilitiesJson=N'{"scope":"custom"}',
              UpdatedAt='20261105' WHERE BusinessInboundContactId=@Contact;
            """, new SqlParameter("@Contact", contactId));
        var edited = await SnapshotAsync();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ExecuteAsync(seed);
            Assert.Equal(edited, await SnapshotAsync());
        }

        // A contact already owning the seeded business/phone keeps its own ID
        // and configuration as well; the seed must not rekey or reactivate it.
        await ExecuteAsync("""
            UPDATE dbo.BusinessInboundContacts SET BusinessInboundContactId=@Other,
              PhoneNumber=@Phone,PhoneNormalized=@Normalized WHERE BusinessInboundContactId=@Contact;
            """, new SqlParameter("@Other", Guid.NewGuid()), new SqlParameter("@Phone", phone),
            new SqlParameter("@Normalized", normalized), new SqlParameter("@Contact", contactId));
        var existingPhone = await SnapshotAsync();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ExecuteAsync(seed);
            Assert.Equal(existingPhone, await SnapshotAsync());
        }
        await transaction.RollbackAsync();

        async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync();
        }

        async Task<string> SnapshotAsync()
        {
            await using var command = new SqlCommand("""
                SELECT * FROM dbo.BusinessInboundContacts WHERE BusinessId=@Business
                ORDER BY BusinessInboundContactId FOR JSON PATH,INCLUDE_NULL_VALUES;
                """, connection, transaction);
            command.Parameters.AddWithValue("@Business", businessId);
            return (string)(await command.ExecuteScalarAsync())!;
        }
    }
}
