using System.Diagnostics;
using System.Net.Http.Json;
using Auraly.Contracts.Orders;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OrderCreatorFilterTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Creator_filter_preserves_original_actor_legacy_identity_tenant_scope_and_server_pagination()
    {
        var other=Guid.NewGuid();var prefix=$"CREATOR-{Guid.NewGuid():N}";
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var seed=new SqlCommand("""
            INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName)
            VALUES(@Other,@Tenant,@Prefix,@Prefix,CONCAT(@Prefix,N'@test.local'),CONCAT(@Prefix,N'@test.local'),N'Otro',N'Creador');
            ;WITH Numbers AS (SELECT TOP(2000) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) n FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT dbo.Orders(OrderId,BusinessId,Source,Status,Currency,CustomerConfirmed,ExternalDocumentNumber,
              CapturedByUserId,CustomAttributesJson,CreatedAt)
            SELECT NEWID(),@Business,1,2,N'COP',1,CONCAT(@Prefix,N'-',n),
              CASE WHEN n%4=0 THEN @User WHEN n%4=1 THEN @Other END,
              CASE WHEN n%4=2 THEN CONCAT(N'{"createdBy":"',@User,N'"}')
                WHEN n%4=0 THEN CONCAT(N'{"createdBy":"',@Other,N'"}') ELSE N'<legacy>' END,
              DATEADD(second,n,CONVERT(datetime2,'20261101')) FROM Numbers;
            """,connection);
        seed.Parameters.AddWithValue("@Other",other);seed.Parameters.AddWithValue("@User",fixture.UserId);
        seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);seed.Parameters.AddWithValue("@Business",fixture.BusinessId);
        seed.Parameters.AddWithValue("@Prefix",prefix);await seed.ExecuteNonQueryAsync();
        using var client=fixture.CreateAdminClient(OrderPermissionCodes.Read);
        var query=$"/api/commerce/v1/orders?orderNumber={prefix}&pageSize=20&createdByUserId={fixture.UserId:D}";
        await Read(query); // Warm the transport and SQL plan before measuring the two pages.
        foreach(var page in new[]{1,2})
        {
            using var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
            using var listCounter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database,commandFilter:command=>command.Parameters.Contains("@CreatedByUserId"));
            var watch=Stopwatch.StartNew();var result=await Read($"{query}&page={page}");watch.Stop();
            Assert.Equal(1000,result.TotalCount);Assert.Equal(20,result.Items.Count);
            Assert.All(result.Items,item=>{Assert.Equal(fixture.UserId,item.CreatedByUserId);Assert.False(string.IsNullOrWhiteSpace(item.CreatedByName));});
            Assert.Equal(1,listCounter.Count);Assert.InRange(counter.Count,1,3);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(1),$"Page {page}: {watch.Elapsed}");
            output.WriteLine($"Creator filter, 2000 orders, page {page}: {watch.Elapsed.TotalMilliseconds:F1} ms, {listCounter.Count} list SQL command, {counter.Count} including request authorization");
        }
        var beyond=await Read($"{query}&page=100");Assert.Empty(beyond.Items);Assert.Equal(1000,beyond.TotalCount);
        Assert.Empty((await Read($"{query}&onlyMine=true&createdFrom=2027-01-01T00:00:00Z")).Items);
        var others=await Read($"/api/commerce/v1/orders?orderNumber={prefix}&createdByUserId={other:D}");
        Assert.Equal(500,others.TotalCount);Assert.All(others.Items,item=>Assert.Equal("Otro Creador",item.CreatedByName));
        Assert.Empty((await Read($"/api/commerce/v1/orders?orderNumber={prefix}&createdByUserId={other:D}&onlyMine=true")).Items);
        // The store must scope the same existing business to the actor tenant even with an arbitrary creator ID.
        using var scope=fixture.CreateScope();
        var store=scope.ServiceProvider.GetService(typeof(Auraly.Application.Orders.IOrderStore)) as Auraly.Application.Orders.IOrderStore;
        var actor=new Auraly.Application.Orders.OrderActor(fixture.UserId,Guid.NewGuid(),fixture.BusinessId,null,null,
            new HashSet<string>{OrderPermissionCodes.Read});
        Assert.Empty((await store!.PageAsync(actor,new(CreatedByUserId:fixture.UserId),CancellationToken.None)).Items);

        async Task<OrderPage> Read(string url)
        {using var response=await client.GetAsync(url);response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<OrderPage>())!;}
    }
}
