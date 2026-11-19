using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class DispatchCaptureTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Provider_timeout_rolls_back_the_dispatch_and_remains_a_provider_failure()
    {
        var dispatchId=Guid.NewGuid();
        await using var setup=new SqlConnection(fixture.ConnectionString);
        await setup.OpenAsync();
        await using(var trigger=new SqlCommand("CREATE TRIGGER dbo.DispatchTimeoutProbe ON dbo.Dispatches AFTER INSERT AS WAITFOR DELAY '00:00:03';",setup))
            await trigger.ExecuteNonQueryAsync();
        try {
            var limited=new SqlConnectionStringBuilder(fixture.ConnectionString){CommandTimeout=1};
            var store=new Auraly.Infrastructure.Dispatching.SqlDispatchStore(new(
                new Auraly.BuildingBlocks.Infrastructure.Persistence.AuralySqlConnectionSource(limited.ConnectionString)));
            var request=new CreateDispatchRequest(fixture.BusinessId,new(2026,11,18),"Prueba de timeout",null,null,null,null,[Guid.NewGuid()]);
            var failure=await Assert.ThrowsAsync<SqlException>(()=>store.CreateAsync(Actor(),dispatchId,$"DSP-TIMEOUT-{dispatchId:N}",
                request,request.DriverName,null,null,DateTimeOffset.UtcNow,CancellationToken.None));
            Assert.Equal(-2,failure.Number);
            await using var count=new SqlCommand("SELECT COUNT(*) FROM dbo.Dispatches WHERE DispatchId=@Id",setup);
            count.Parameters.AddWithValue("@Id",dispatchId);
            Assert.Equal(0,Convert.ToInt32(await count.ExecuteScalarAsync()));
        } finally {
            await using var cleanup=new SqlCommand("DROP TRIGGER IF EXISTS dbo.DispatchTimeoutProbe",setup);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Drivers_are_paged_without_parameter_collision_and_only_eligible_users_are_returned()
    {
        var marker = $"DRIVER-{Guid.NewGuid():N}";
        var role = Guid.NewGuid();
        var users = Enumerable.Range(0, 103).Select(index => new { Id = Guid.NewGuid(), Index = index }).ToArray();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName) VALUES(@Role,@Tenant,@Marker,@Marker);
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId)
              SELECT NEWID(),@Role,PermissionId FROM dbo.Permissions WHERE Resource=N'dispatches.delivery.execute';
            INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,IsActive)
              SELECT Id,@Tenant,CONVERT(nvarchar(36),Id),CONVERT(nvarchar(36),Id),CONCAT(Id,N'@test.invalid'),
                CONCAT(Id,N'@test.invalid'),@Marker,CONVERT(nvarchar(3),[Index]),CASE WHEN [Index]=101 THEN 0 ELSE 1 END
              FROM OPENJSON(@Users) WITH(Id uniqueidentifier,[Index] int);
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId)
              SELECT NEWID(),Id,@Role,CASE WHEN [Index]%2=0 THEN @Business ELSE NULL END
              FROM OPENJSON(@Users) WITH(Id uniqueidentifier,[Index] int) WHERE [Index]<102;
            """, connection);
        seed.Parameters.AddWithValue("@Role", role); seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        seed.Parameters.AddWithValue("@Business", fixture.BusinessId); seed.Parameters.AddWithValue("@Marker", marker);
        seed.Parameters.AddWithValue("@Users", JsonSerializer.Serialize(users));
        await seed.ExecuteNonQueryAsync();
        using var client = fixture.CreateAdminClient(DispatchPermissionCodes.Create);
        var page = (await client.GetFromJsonAsync<DispatchDriverPage>(
            $"/api/commerce/v1/dispatches/drivers?page=1&pageSize=50&search={marker}"))!;
        Assert.Equal(101, page.TotalCount); Assert.Equal(50, page.Items.Count);
        var next = (await client.GetFromJsonAsync<DispatchDriverPage>(
            $"/api/commerce/v1/dispatches/drivers?page=2&pageSize=50&search={marker}"))!;
        Assert.Equal(100, page.Items.Concat(next.Items).Select(value => value.UserId).Distinct().Count());
        var selected = page.Items.First();
        var exact = (await client.GetFromJsonAsync<DispatchDriverPage>(
            $"/api/commerce/v1/dispatches/drivers?userId={selected.UserId}"))!;
        Assert.Equal(selected, Assert.Single(exact.Items));
        Assert.Empty((await client.GetFromJsonAsync<DispatchDriverPage>(
            $"/api/commerce/v1/dispatches/drivers?userId={users[101].Id}"))!.Items);
        Assert.Empty((await client.GetFromJsonAsync<DispatchDriverPage>(
            $"/api/commerce/v1/dispatches/drivers?userId={users[102].Id}"))!.Items);
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDispatchStore>();
        using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
        await store.DriversAsync(Actor(), 1, 20, marker, null, CancellationToken.None);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task Candidate_business_days_page_before_details_and_one_dispatch_can_group_two_source_warehouses()
    {
        var marker = $"D{Guid.NewGuid():N}"[..8];
        var otherWarehouse = Guid.NewGuid();
        var docs = Enumerable.Range(0, 2000).Select(index => new {
            Id = Guid.NewGuid(), Index = index,
            At = index switch {
                0 => DateTimeOffset.Parse("2026-11-16T04:59:59Z"),
                1 => DateTimeOffset.Parse("2026-11-16T05:00:00Z"),
                1998 => DateTimeOffset.Parse("2026-11-17T04:59:59Z"),
                1999 => DateTimeOffset.Parse("2026-11-17T05:00:00Z"),
                _ => DateTimeOffset.Parse("2026-11-16T12:00:00Z").AddSeconds(index)
            }
        }).ToArray();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,IsActive,UseForSales,CreatedAt,AllowNegativeStockSales)
              VALUES(@OtherWarehouse,@Business,@Marker,N'Otra bodega del despacho',1,1,SYSUTCDATETIME(),0);
            INSERT dbo.SalesDocuments(DocumentId,BusinessId,WarehouseId,SourceMode,DocumentSeriesId,
              DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,DocumentType,IdempotencyKey,
              PayloadHash,IssuedAt,CustomerIdentification,UntaxedAmount,TaxAmount,PayableAmount,ProcessingStatus,ReceivedAt)
              SELECT Id,@Business,CASE WHEN [Index]=2 THEN @OtherWarehouse ELSE @Warehouse END,N'Online',@Series,
                CONCAT(@Marker,N'-',[Index]),@Marker,N'QA',[Index]+1,N'SalesReceipt',CONVERT(nvarchar(36),Id),
                HASHBYTES('SHA2_256',CONVERT(nvarchar(36),Id)),[At],N'222222222222',3,0,3,N'Completed',[At]
              FROM OPENJSON(@Docs) WITH(Id uniqueidentifier,[Index] int,[At] datetimeoffset(7));
            INSERT dbo.SalesDocumentLines(DocumentId,LineNumber,ProductId,Description,TaxCode,TaxRate,
              Quantity,UnitPrice,DiscountAmount,TaxAmount,UntaxedAmount,LineTotal)
              SELECT Id,n.Value,@Product,N'Producto del despacho',N'01',0,1,1,0,0,1,1
              FROM OPENJSON(@Docs) WITH(Id uniqueidentifier) CROSS JOIN (VALUES(1),(2),(3)) n(Value);
            """, connection);
        seed.Parameters.AddWithValue("@Business", fixture.BusinessId); seed.Parameters.AddWithValue("@Warehouse", fixture.WarehouseId);
        seed.Parameters.AddWithValue("@OtherWarehouse", otherWarehouse); seed.Parameters.AddWithValue("@Marker", marker);
        seed.Parameters.AddWithValue("@Series", fixture.OnlineSalesReceiptSeriesId); seed.Parameters.AddWithValue("@Product", fixture.ProductId);
        seed.Parameters.AddWithValue("@Docs", JsonSerializer.Serialize(docs));
        await seed.ExecuteNonQueryAsync();
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDispatchStore>();
        var query = new DispatchCandidateQuery(1, 20, marker, From: new(2026, 11, 16), To: new(2026, 11, 16), DateRangeVersion: 2);
        var timings = new List<double>();
        var ids = new HashSet<Guid>();
        for (var sample = 0; sample < 9; sample++)
        foreach (var pageNumber in new[] { 1, 2 })
        {
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
            var clock = Stopwatch.StartNew();
            var page = await store.CandidatesAsync(Actor(), query with { Page = pageNumber }, CancellationToken.None);
            clock.Stop();
            Assert.Equal(2, counter.Count); // Time zone, then one count/page command.
            Assert.Equal(1998, page.TotalCount); Assert.Equal(20, page.Items.Count);
            Assert.All(page.Items, value => Assert.Equal(3, value.LineCount));
            if (sample == 0) foreach (var row in page.Items) Assert.True(ids.Add(row.DocumentId));
            else timings.Add(clock.Elapsed.TotalMilliseconds);
        }
        var p95 = timings.Order().ElementAt((int)Math.Ceiling(timings.Count * .95) - 1);
        output.WriteLine("Dispatch candidates: 2 SQL commands, 2,000 headers / 6,000 lines, page 1/2 p95 {0:F2} ms", p95);
        Assert.True(p95 < 1000, $"Dispatch candidate p95 {p95:F2} ms exceeded one second.");
        var first = await store.CandidatesAsync(Actor(), query, CancellationToken.None);
        Assert.Contains(first.Items, value => value.DocumentId == docs[1998].Id);
        var legacy = await store.CandidatesAsync(Actor(), query with { DateRangeVersion = 1 }, CancellationToken.None);
        Assert.DoesNotContain(legacy.Items, value => value.DocumentId == docs[1998].Id);
        await Assert.ThrowsAsync<DispatchForbiddenException>(() => store.CandidatesAsync(
            Actor() with { TenantId = Guid.NewGuid() }, query, CancellationToken.None));
        using var client = fixture.CreateAdminClient(DispatchPermissionCodes.Create, DispatchPermissionCodes.AttachDocuments,
            DispatchPermissionCodes.Read, DispatchPermissionCodes.ReadAll);
        var request = new CreateDispatchRequest(fixture.BusinessId, new(2026, 11, 16), "Responsable de prueba",
            null, null, null, null, [docs[2].Id, docs[3].Id]);
        using var created = await client.PostAsJsonAsync("/api/commerce/v1/dispatches", request);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var result = (await created.Content.ReadFromJsonAsync<DispatchMutationResult>())!;
        Assert.Equal(2, result.DocumentCount); Assert.Equal(6, result.LineCount); Assert.Equal(6m, result.ExpectedQuantity);
        var detail = (await client.GetFromJsonAsync<DispatchDetail>($"/api/commerce/v1/dispatches/{result.DispatchId}"))!;
        Assert.Equal(new[] { fixture.WarehouseId, otherWarehouse }.Order(), detail.Documents.Select(value => value.WarehouseId).Order());
        await using var verify = new SqlCommand("""
            SELECT WarehouseId FROM dbo.Dispatches WHERE DispatchId=@Dispatch;
            SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId IN(@First,@Second);
            SELECT COUNT(*) FROM dbo.DispatchSourceDocuments WHERE SourceDocumentId IN(@First,@Second);
            """, connection);
        verify.Parameters.AddWithValue("@Dispatch", result.DispatchId); verify.Parameters.AddWithValue("@First", docs[2].Id); verify.Parameters.AddWithValue("@Second", docs[3].Id);
        await using (var reader = await verify.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync()); Assert.True(reader.IsDBNull(0));
            await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal(0, reader.GetInt32(0));
            await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal(2, reader.GetInt32(0));
        }
        // The settlement worker must return each document to its own warehouse,
        // never to the now-absent warehouse of the dispatch header.
        await using var delivery = new SqlCommand("""
            INSERT dbo.DispatchDeliveryEvents(DispatchDeliveryEventId,BusinessId,DispatchId,DispatchSourceDocumentId,
              DeliveryStatus,Reason,OccurredAt,RecordedBy,ReceivedAt,IdempotencyKey)
            SELECT NEWID(),@Business,@Dispatch,DispatchSourceDocumentId,N'NotDelivered',N'Prueba de origen',
              SYSUTCDATETIME(),@User,SYSUTCDATETIME(),CONVERT(nvarchar(36),DispatchSourceDocumentId)
            FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Dispatch;
            """, connection);
        delivery.Parameters.AddWithValue("@Business", fixture.BusinessId); delivery.Parameters.AddWithValue("@Dispatch", result.DispatchId);
        delivery.Parameters.AddWithValue("@User", fixture.UserId); await delivery.ExecuteNonQueryAsync();
        await using var returns = new SqlCommand("dbo.DispatchSettlementReturnsGet", connection) { CommandType = System.Data.CommandType.StoredProcedure };
        returns.Parameters.AddWithValue("@BusinessId", fixture.BusinessId); returns.Parameters.AddWithValue("@DispatchId", result.DispatchId);
        await using var returned = await returns.ExecuteReaderAsync();
        var rows = 0;
        while (await returned.ReadAsync())
        {
            Assert.Equal(returned.GetGuid(0) == docs[2].Id ? otherWarehouse : fixture.WarehouseId, returned.GetGuid(6));
            rows++;
        }
        Assert.Equal(6, rows);
        await returned.DisposeAsync();

        // One aggregate is bounded at 500 documents / 10,000 product lines.
        // Its execution read has a constant number of SQL commands at either size.
        using var largeResponse=await client.PostAsJsonAsync("/api/commerce/v1/dispatches",
            request with{SourceDocumentIds=docs.Skip(10).Take(500).Select(item=>item.Id).ToArray()});
        Assert.True(largeResponse.IsSuccessStatusCode,await largeResponse.Content.ReadAsStringAsync());
        var large=(await largeResponse.Content.ReadFromJsonAsync<DispatchMutationResult>())!;
        var deliveryStore=scope.ServiceProvider.GetRequiredService<IDispatchDeliveryStore>();
        var readerActor=Actor() with{Permissions=new HashSet<string>{DispatchPermissionCodes.ReadAll,DispatchPermissionCodes.ExecuteDeliveries},IsAdministrator=true};
        foreach(var sample in new[]{(result.DispatchId,2),(large.DispatchId,500)})
        {
            await deliveryStore.GetAsync(readerActor,sample.DispatchId,default);
            using var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
            var clock=Stopwatch.StartNew();
            var execution=(await deliveryStore.GetAsync(readerActor,sample.DispatchId,default))!;
            clock.Stop();
            Assert.Equal(sample.Item2,execution.Documents.Count);
            Assert.Equal(sample.Item2*3,execution.Documents.Sum(item=>item.Lines.Count));
            Assert.Equal(7,counter.Count);
            output.WriteLine("Dispatch execution: {0} documents / {1} lines, 7 SQL commands, {2:F2} ms",sample.Item2,sample.Item2*3,clock.Elapsed.TotalMilliseconds);
            Assert.True(clock.ElapsedMilliseconds<2000,$"Dispatch execution exceeded two seconds: {clock.Elapsed.TotalMilliseconds:F2} ms.");
        }
        await using(var release=new SqlCommand("UPDATE dbo.Dispatches SET Status=N'Released',DriverUserId=@User WHERE DispatchId=@Id;",connection))
        {release.Parameters.AddWithValue("@User",fixture.UserId);release.Parameters.AddWithValue("@Id",large.DispatchId);await release.ExecuteNonQueryAsync();}
        var version=await store.GetAsync(readerActor,large.DispatchId,default);
        var executionBefore=(await deliveryStore.GetAsync(readerActor,large.DispatchId,default))!;
        var order=executionBefore.Documents.Select(item=>item.DispatchSourceDocumentId).Reverse().ToArray();
        var reorder=new ReorderDispatchDocumentsRequest(order,version!.RowVersion);
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var changed=await deliveryStore.ReorderAsync(readerActor,large.DispatchId,reorder,Convert.FromBase64String(version.RowVersion),default);
            Assert.Equal(order,changed.Documents.Select(item=>item.DispatchSourceDocumentId));
            Assert.Equal(8,counter.Count); // One set write plus the authoritative seven-command result.
        }
        var afterVersion=(await store.GetAsync(readerActor,large.DispatchId,default))!.RowVersion;
        await deliveryStore.ReorderAsync(readerActor,large.DispatchId,reorder,Convert.FromBase64String(version.RowVersion),default);
        Assert.Equal(afterVersion,(await store.GetAsync(readerActor,large.DispatchId,default))!.RowVersion);
        await Assert.ThrowsAsync<DispatchConflictException>(()=>deliveryStore.ReorderAsync(readerActor,large.DispatchId,
            reorder with{OrderedDocumentIds=order.Reverse().ToArray()},Convert.FromBase64String(version.RowVersion),default));
    }

    [Theory]
    [InlineData("2026-11-16", null)]
    [InlineData(null, "2026-11-16")]
    [InlineData("2026-11-17", "2026-11-16")]
    public async Task Candidate_business_day_contract_rejects_incomplete_or_reversed_ranges(string? from, string? to)
    {
        using var client = fixture.CreateAdminClient(DispatchPermissionCodes.AttachDocuments);
        using var response = await client.GetAsync($"/api/commerce/v1/dispatches/candidates?page=1&pageSize=20&dateRangeVersion=2&from={from}&to={to}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private DispatchActorIdentity Actor() => new(fixture.UserId, fixture.TenantId, fixture.BusinessId,
        new HashSet<string> { DispatchPermissionCodes.Read, DispatchPermissionCodes.Create, DispatchPermissionCodes.AttachDocuments }, false);
}
