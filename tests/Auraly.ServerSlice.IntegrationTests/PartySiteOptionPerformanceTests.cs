using System.Diagnostics;
using Auraly.Application.Parties;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PartySiteOptionPerformanceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Portfolio site-option performance";
}

[Collection(PartySiteOptionPerformanceCollection.Name)]
public sealed class PartySiteOptionPerformanceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Rare_customer_and_supplier_site_searches_and_later_pages_use_one_bounded_command()
    {
        // An isolated fixture owns these 7,000 parties and drops its database.
        // The rare last-sorting name reproduces the optimistic TOP row-goal plan.
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            DECLARE @Rows TABLE(PartyId uniqueidentifier,Number int);
            WITH Digits AS(SELECT N FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(N))
            INSERT @Rows SELECT NEWID(),a.N+10*b.N+100*c.N+1000*d.N
              FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c CROSS JOIN Digits d
              WHERE a.N+10*b.N+100*c.N+1000*d.N<7000;
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedAt)
              SELECT PartyId,@Tenant,N'Organization',
                CASE WHEN Number=6999 THEN N'ZZZ Único buscado' ELSE N'Cliente '+CONVERT(nvarchar(8),Number) END,
                N'Incomplete',1,SYSUTCDATETIME() FROM @Rows;
            INSERT dbo.Customers(CustomerId,PartyId,TenantId,IsActive,CreatedAt)
              SELECT NEWID(),PartyId,@Tenant,1,SYSUTCDATETIME() FROM @Rows;
            INSERT dbo.Suppliers(SupplierId,PartyId,TenantId,Identification,Name,IsActive,CreatedAt)
              SELECT NEWID(),PartyId,@Tenant,CONVERT(nvarchar(36),PartyId),N'Proveedor de prueba',1,SYSUTCDATETIME() FROM @Rows;
            INSERT dbo.PartyContacts(PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
              SELECT NEWID(),PartyId,N'Email',CONCAT(N'party-',Number,N'@performance.test'),CONCAT(N'PARTY-',Number,N'@PERFORMANCE.TEST'),1,1,SYSUTCDATETIME() FROM @Rows
              UNION ALL SELECT NEWID(),PartyId,N'Phone',CONCAT(N'300',Number),CONCAT(N'300',Number),1,1,SYSUTCDATETIME() FROM @Rows;
            INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
              AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
              SELECT NEWID(),row.PartyId,N'PRINCIPAL',N'Sede de prueba',site.CountryId,site.AdministrativeDivisionId,
                site.CityId,N'Dirección de prueba',1,1,@User,SYSUTCDATETIME() FROM @Rows row
              CROSS JOIN dbo.PartySites site WHERE site.PartySiteId=(SELECT TOP(1) PartySiteId
                FROM dbo.PartySites WHERE PartyId=@SupplierParty ORDER BY PartySiteId);
            """, connection);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        seed.Parameters.AddWithValue("@User", fixture.UserId);
        seed.Parameters.AddWithValue("@SupplierParty", fixture.SupplierPartyId);
        await seed.ExecuteNonQueryAsync();
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPartyWorkspaceStore>();
        var actor = new PartyActorIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId, new HashSet<string>());
        foreach (var role in new[] { "Customer", "Supplier" })
        foreach (var (search, page) in new (string?, int)[] { ("ZZZ Unico buscado", 1), ("No existe este tercero", 1), (null, 1), (null, 2) })
        {
            var samples = new List<double>();
            for (var sample = 0; sample < 6; sample++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    commandFilter: command => command.CommandText.Contains("SELECT COUNT_BIG(1)", StringComparison.Ordinal)
                        && command.CommandText.Contains("dbo.PartySites", StringComparison.Ordinal));
                var timer = Stopwatch.StartNew();
                var result = await store.SiteRoleOptionsAsync(actor, page, new(role, 10, search), default);
                timer.Stop();
                Assert.Equal(1, counter.Count);
                if (search == "ZZZ Unico buscado")
                {
                    Assert.Equal(1, result.TotalCount);
                    Assert.Equal("ZZZ Único buscado", Assert.Single(result.Items).DisplayName);
                }
                else if (search is not null) Assert.Empty(result.Items);
                else { Assert.Equal(10, result.Items.Count); Assert.True(result.TotalCount >= 7000); }
                if (sample > 0) samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var worst = samples.Max();
            output.WriteLine("{0}, search={1}, page={2}: max={3:F1}ms, one command, five warm samples", role, search, page, worst);
            Assert.True(worst < 500, $"Site-option search exceeded 500ms: {worst:F1}ms.");
        }
        // The full third-party grid must also filter and page before loading role/contact details.
        foreach (var role in new string?[] { null, "Customer", "Supplier" })
        foreach (var (search, page) in new (string?, int)[] { ("ZZZ Único buscado", 1), ("party-6999@performance.test", 1), ("Único performance.test", 1), ("No existe este tercero", 1), ("Cliente", 1), ("Cliente", 2), (null, 1), (null, 2) })
        {
            var samples = new List<double>();
            for (var sample = 0; sample < 6; sample++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    commandFilter: command => command.CommandText.Contains("SELECT COUNT_BIG(1)", StringComparison.Ordinal)
                        && command.CommandText.Contains("@Incomplete", StringComparison.Ordinal));
                var timer = Stopwatch.StartNew();
                var result = await store.PageAsync(actor, page, new(25, search, role), default);
                timer.Stop();
                Assert.Equal(1, counter.Count);
                if (search is "ZZZ Único buscado" or "party-6999@performance.test" or "Único performance.test") Assert.Equal("ZZZ Único buscado", Assert.Single(result.Items).DisplayName);
                else if (search == "No existe este tercero") Assert.Empty(result.Items);
                else { Assert.Equal(25, result.Items.Count); Assert.True(result.TotalCount >= 6999); }
                if (sample > 0) samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var worst = samples.Max();
            output.WriteLine("Third-party grid, role={0}, search={1}, page={2}: max={3:F1}ms, one command, five warm samples", role, search, page, worst);
            Assert.True(worst < 500, $"Third-party grid exceeded 500ms: {worst:F1}ms.");
        }
    }
}
