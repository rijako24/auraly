using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Parties;
using Auraly.Contracts.Routes;
using Auraly.Application.Parties;
using Auraly.Infrastructure.Persistence;
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
        var carrierId=await fixture.SeedDispatchCarrierAsync();
        await using var setup=new SqlConnection(fixture.ConnectionString);
        await setup.OpenAsync();
        await using(var trigger=new SqlCommand("CREATE TRIGGER dbo.DispatchTimeoutProbe ON dbo.Dispatches AFTER INSERT AS WAITFOR DELAY '00:00:03';",setup))
            await trigger.ExecuteNonQueryAsync();
        try {
            var limited=new SqlConnectionStringBuilder(fixture.ConnectionString){CommandTimeout=1};
            var store=new Auraly.Infrastructure.Dispatching.SqlDispatchStore(new(
                new Auraly.BuildingBlocks.Infrastructure.Persistence.AuralySqlConnectionSource(limited.ConnectionString)),new SqlOperationalDocumentAllocator());
            var request=new CreateDispatchRequest(fixture.BusinessId,new(2026,11,18),"Prueba de timeout",null,carrierId,null,[Guid.NewGuid()]);
            var failure=await Assert.ThrowsAsync<SqlException>(()=>store.CreateAsync(Actor(),dispatchId,
                request,null,null,DateTimeOffset.UtcNow,CancellationToken.None));
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
    public async Task Carrier_selector_uses_the_party_catalog_and_excludes_inactive_parties_and_roles()
    {
        var marker = $"CARRIER-{Guid.NewGuid():N}";
        var carriers = Enumerable.Range(0, 103).Select(index => new { Id = Guid.NewGuid(), Party = Guid.NewGuid(), Index = index }).ToArray();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedAt)
              SELECT Party,@Tenant,N'NaturalPerson',CONCAT(@Marker,[Index]),N'Incomplete',
                CASE WHEN [Index]=102 THEN 0 ELSE 1 END,SYSUTCDATETIME()
              FROM OPENJSON(@Carriers) WITH(Id uniqueidentifier,Party uniqueidentifier,[Index] int);
            INSERT dbo.Carriers(CarrierId,TenantId,PartyId,Code,TransportationMode,IsActive,CreatedAt)
              SELECT Id,@Tenant,Party,LEFT(CONVERT(nvarchar(36),Id),32),N'Road',
                CASE WHEN [Index]=101 THEN 0 ELSE 1 END,SYSUTCDATETIME()
              FROM OPENJSON(@Carriers) WITH(Id uniqueidentifier,Party uniqueidentifier,[Index] int);
            """, connection);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        seed.Parameters.AddWithValue("@Marker", marker);
        seed.Parameters.AddWithValue("@Carriers", JsonSerializer.Serialize(carriers));
        await seed.ExecuteNonQueryAsync();
        using var client = fixture.CreateAdminClient(DispatchPermissionCodes.Create);
        var path = $"/api/commerce/v1/parties/role-options?role=Carrier&pageSize=50&search={marker}";
        var page = (await client.GetFromJsonAsync<PartyRoleOptionPage>(path + "&page=1"))!;
        var next = (await client.GetFromJsonAsync<PartyRoleOptionPage>(path + "&page=2"))!;
        Assert.Equal(101, page.TotalCount); Assert.Equal(50, page.Items.Count);
        Assert.Equal(100, page.Items.Concat(next.Items).Select(value => value.RoleId).Distinct().Count());
        Assert.All(page.Items, value => Assert.Equal("Carrier", value.Role));
        var selected = page.Items.First();
        var exact = (await client.GetFromJsonAsync<PartyRoleOptionPage>(
            $"/api/commerce/v1/parties/role-options?role=Carrier&roleId={selected.RoleId}"))!;
        Assert.Equal(selected, Assert.Single(exact.Items));
        foreach (var inactive in carriers.Skip(101))
            Assert.Empty((await client.GetFromJsonAsync<PartyRoleOptionPage>(
                $"/api/commerce/v1/parties/role-options?role=Carrier&roleId={inactive.Id}"))!.Items);
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPartyWorkspaceStore>();
        using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
        await store.RoleOptionsAsync(new(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{DispatchPermissionCodes.Create}),1,new("Carrier",20,marker),CancellationToken.None);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task Candidate_business_days_page_before_details_and_one_dispatch_can_group_two_source_warehouses()
    {
        var marker = $"D{Guid.NewGuid():N}"[..8];
        var carrierId=await fixture.SeedDispatchCarrierAsync();
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
        var routes=Enumerable.Range(0,31).Select(index=>new{Id=Guid.NewGuid(),Index=index}).ToArray();
        await using(var routeSeed=new SqlCommand("""
          DECLARE @Party uniqueidentifier=NEWID(),@Customer uniqueidentifier=NEWID(),@Site uniqueidentifier=NEWID(),@Seller uniqueidentifier=NEWID();
          INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@Party,@Tenant,N'NaturalPerson',N'Cliente de ruta',N'Incomplete',1,SYSUTCDATETIME());
          INSERT dbo.Customers(CustomerId,TenantId,PartyId,IsActive,CreatedAt)
            VALUES(@Customer,@Tenant,@Party,1,SYSUTCDATETIME());
          INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
            SELECT TOP(1) @Site,@Party,N'PRINCIPAL',N'Destino de la ruta',CountryId,AdministrativeDivisionId,CityId,N'Dirección de prueba',1,1,@User,SYSUTCDATETIME()
            FROM dbo.PartySites ORDER BY PartySiteId;
          INSERT dbo.CommerceSellers(SellerId,TenantId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
            SELECT @Seller,@Tenant,PartyId,@Marker,N'SaleBeforeTax',N'Sale',1,SYSUTCDATETIME() FROM dbo.Carriers WHERE CarrierId=@Carrier;
          INSERT dbo.SalesRoutes(RouteId,BusinessId,Code,Name,SellerId,IsActive,CreatedBy,CreatedAt)
            SELECT Id,@Business,CONCAT(@Marker,[Index]),CONCAT(@Marker,N' ruta ',[Index]),@Seller,
              CASE WHEN [Index]=30 THEN 0 ELSE 1 END,@User,SYSUTCDATETIME()
            FROM OPENJSON(@Routes) WITH(Id uniqueidentifier,[Index] int);
          INSERT dbo.SalesRouteStops(RouteStopId,RouteId,CustomerId,PartySiteId,Sequence,IsActive,CreatedBy,CreatedAt)
            VALUES(NEWID(),@Route,@Customer,@Site,1,1,@User,SYSUTCDATETIME());
          UPDATE dbo.SalesDocuments SET CustomerId=@Customer,CustomerPartySiteId=@Site WHERE DocumentId IN(@First,@Second);
          """,connection))
        {
            routeSeed.Parameters.AddWithValue("@Tenant",fixture.TenantId);routeSeed.Parameters.AddWithValue("@Business",fixture.BusinessId);
            routeSeed.Parameters.AddWithValue("@Carrier",carrierId);routeSeed.Parameters.AddWithValue("@Marker",marker);
            routeSeed.Parameters.AddWithValue("@Routes",JsonSerializer.Serialize(routes));routeSeed.Parameters.AddWithValue("@Route",routes[0].Id);
            routeSeed.Parameters.AddWithValue("@User",fixture.UserId);routeSeed.Parameters.AddWithValue("@First",docs[2].Id);routeSeed.Parameters.AddWithValue("@Second",docs[3].Id);
            await routeSeed.ExecuteNonQueryAsync();
        }
        using(var routeClient=fixture.CreateAdminClient(DispatchPermissionCodes.Create))
        {
            var path=$"/api/commerce/v1/dispatches/options?pageSize=10&search={marker}";
            var options=(await routeClient.GetFromJsonAsync<SalesRoutePage>(path+"&page=1"))!;
            var next=(await routeClient.GetFromJsonAsync<SalesRoutePage>(path+"&page=2"))!;
            Assert.Equal(30,options.TotalCount);Assert.Equal(10,options.Items.Count);
            Assert.Equal(20,options.Items.Concat(next.Items).Select(row=>row.RouteId).Distinct().Count());
            using var denied=fixture.CreateAdminClient(DispatchPermissionCodes.Read);
            Assert.Equal(HttpStatusCode.Forbidden,(await denied.GetAsync(path)).StatusCode);
        }
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDispatchStore>();
        var query = new DispatchCandidateQuery(1, 20, marker, From: new(2026, 11, 16), To: new(2026, 11, 16), DateRangeVersion: 2);
        var routeOnly=await store.CandidatesAsync(Actor(),query with{RouteId=routes[0].Id},default);
        Assert.Equal(2,routeOnly.TotalCount);Assert.Equal(docs.Skip(2).Take(2).Select(row=>row.Id).Order(),routeOnly.Items.Select(row=>row.DocumentId).Order());
        Assert.Empty((await store.CandidatesAsync(Actor(),query with{RouteId=Guid.NewGuid()},default)).Items);
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
            null, carrierId, null, [docs[2].Id, docs[3].Id]);
        using var created = await client.PostAsJsonAsync("/api/commerce/v1/dispatches", request);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var result = (await created.Content.ReadFromJsonAsync<DispatchMutationResult>())!;
        Assert.StartsWith("DSP",result.DispatchNumber); Assert.DoesNotContain("-2026",result.DispatchNumber);
        Assert.Equal(2, result.DocumentCount); Assert.Equal(6, result.LineCount); Assert.Equal(6m, result.ExpectedQuantity);
        var detail = (await client.GetFromJsonAsync<DispatchDetail>($"/api/commerce/v1/dispatches/{result.DispatchId}"))!;
        Assert.Equal(new[] { fixture.WarehouseId, otherWarehouse }.Order(), detail.Documents.Select(value => value.WarehouseId).Order());
        await using var verify = new SqlCommand("""
            SELECT WarehouseId,RouteId FROM dbo.Dispatches WHERE DispatchId=@Dispatch;
            SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId IN(@First,@Second);
            SELECT COUNT(*) FROM dbo.DispatchSourceDocuments WHERE SourceDocumentId IN(@First,@Second);
            """, connection);
        verify.Parameters.AddWithValue("@Dispatch", result.DispatchId); verify.Parameters.AddWithValue("@First", docs[2].Id); verify.Parameters.AddWithValue("@Second", docs[3].Id);
        await using (var reader = await verify.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync()); Assert.True(reader.IsDBNull(0)); Assert.True(reader.IsDBNull(1));
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
        // Admin pagination stays bounded before details are materialized. The
        // summary covers the whole dispatch, not merely the visible ten rows.
        var seenDocuments=new HashSet<Guid>(); var seenLines=new HashSet<Guid>();
        foreach(var pageNumber in new[]{1,2})
        {
            await store.GetAsync(Actor(),large.DispatchId,default,new(pageNumber,10,pageNumber,10));
            using var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
            var clock=Stopwatch.StartNew();
            var paged=(await store.GetAsync(Actor(),large.DispatchId,default,new(pageNumber,10,pageNumber,10)))!;
            clock.Stop();
            Assert.Equal(4,counter.Count); Assert.Equal(10,paged.Documents.Count); Assert.Equal(10,paged.Lines.Count);
            Assert.Equal(500,paged.Summary!.DocumentCount); Assert.Equal(1500,paged.Summary.LineCount);
            foreach(var row in paged.Documents)Assert.True(seenDocuments.Add(row.SourceDocumentId));
            foreach(var row in paged.Lines)Assert.True(seenLines.Add(row.DispatchLineId));
            output.WriteLine("Dispatch detail page {0}: 10 documents / 10 lines, 4 SQL commands, {1:F2} ms",pageNumber,clock.Elapsed.TotalMilliseconds);
            Assert.True(clock.ElapsedMilliseconds<2000);
        }
        var service=scope.ServiceProvider.GetRequiredService<DispatchService>();
        var verifier=Actor() with{Permissions=new HashSet<string>{DispatchPermissionCodes.Read,DispatchPermissionCodes.Prepare,DispatchPermissionCodes.Verify}};
        var prepared=await service.PrepareAsync(verifier,result.DispatchId,new(result.RowVersion,Guid.NewGuid().ToString("N")),default);
        await service.StartVerificationAsync(verifier,result.DispatchId,new(prepared.RowVersion,Guid.NewGuid().ToString("N")),default);
        var line=detail.Lines.First();
        await using(var fractional=new SqlCommand("UPDATE dbo.DispatchLines SET AssignedQuantity=20.5 WHERE DispatchLineId=@Id",connection))
        {fractional.Parameters.AddWithValue("@Id",line.DispatchLineId);await fractional.ExecuteNonQueryAsync();}
        var verification=new DispatchVerificationRequest(line.DispatchLineId,20.5m,null,Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow);
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var verified=await service.VerifyQuantityAsync(verifier,result.DispatchId,verification,default);
            Assert.Equal(2,counter.Count); Assert.Equal(20.5m,verified.UpdatedLine!.VerifiedQuantity);
            Assert.Equal("Verified",verified.UpdatedLine.Status); Assert.Equal(5,verified.PendingLineCount);
        }
        var replay=await service.VerifyQuantityAsync(verifier,result.DispatchId,verification,default);
        Assert.Equal(20.5m,replay.UpdatedLine!.VerifiedQuantity);
        var deliveryStore=scope.ServiceProvider.GetRequiredService<IDispatchDeliveryStore>();
        var readerActor=Actor() with{Permissions=new HashSet<string>{DispatchPermissionCodes.ReadAll,DispatchPermissionCodes.ExecuteDeliveries},IsAdministrator=true};
        var unrelated=readerActor with{UserId=Guid.NewGuid()};
        Assert.Null(await deliveryStore.GetAsync(unrelated,large.DispatchId,default));
        Assert.Empty((await store.PageAsync(unrelated,new(1,10,large.DispatchNumber,Mine:true),default)).Items);
        Assert.NotNull(await deliveryStore.GetAsync(unrelated with{Permissions=new HashSet<string>{DispatchPermissionCodes.Settle}},large.DispatchId,default,true));
        Assert.Null(await deliveryStore.GetAsync(readerActor with{TenantId=Guid.NewGuid()},large.DispatchId,default));
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
