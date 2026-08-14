using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Routes;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RoutesVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Route_can_be_created_scheduled_built_reordered_and_filtered_end_to_end()
    {
        var seed = await SeedCommercialPartiesAsync();
        using var client = fixture.CreateAdminClient(
            RoutePermissionCodes.Read, RoutePermissionCodes.Create, RoutePermissionCodes.Update,
            RoutePermissionCodes.ManageStops, RoutePermissionCodes.ManageZones,
            RoutePermissionCodes.Activate, RoutePermissionCodes.Deactivate, RoutePermissionCodes.Export,
            RoutePermissionCodes.RecordVisits, RoutePermissionCodes.ReadAll);

        var zoneResponse = await client.PostAsJsonAsync("/api/commerce/v1/route-zones",
            new CreateSalesZoneRequest(fixture.BusinessId, $"ZN-{Guid.NewGuid():N}"[..12], "Zona norte"));
        Assert.Equal(HttpStatusCode.Created, zoneResponse.StatusCode);
        var zone = await zoneResponse.Content.ReadFromJsonAsync<SalesZoneItem>();
        Assert.NotNull(zone);

        var create = new CreateSalesRouteRequest(fixture.BusinessId, $"RT-{Guid.NewGuid():N}"[..12],
            "Ruta de prueba", seed.SellerId, zone.ZoneId, "Recorrido E2E",
            [new(1, 1, new TimeOnly(8, 0)), new(3, 1, null)]);
        var createdResponse = await client.PostAsJsonAsync("/api/commerce/v1/routes", create);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<RouteMutationResult>();
        Assert.NotNull(created);
        Assert.Equal("Draft", created.PreparationStatus);

        var candidates = await client.GetFromJsonAsync<RouteCandidatePage>(
            $"/api/commerce/v1/routes/{created.RouteId:D}/candidate-sites?page=1&pageSize=50&search={seed.SearchTerm}");
        Assert.NotNull(candidates);
        Assert.Equal(2, candidates.TotalCount);
        Assert.All(candidates.Items, item => Assert.False(item.HasScheduleConflict));

        var firstAdd = await client.PostAsJsonAsync($"/api/commerce/v1/routes/{created.RouteId:D}/stops",
            new AddRouteStopsRequest([
                new(seed.CustomerOneId, seed.SiteOneId, "Primera visita"),
                new(seed.CustomerTwoId, seed.SiteTwoId, null)], created.RowVersion));
        Assert.Equal(HttpStatusCode.OK, firstAdd.StatusCode);
        var afterAdd = await firstAdd.Content.ReadFromJsonAsync<RouteMutationResult>();
        Assert.NotNull(afterAdd);
        Assert.Equal("Ready", afterAdd.PreparationStatus);
        Assert.Equal(2, afterAdd.StopCount);

        var detail = await client.GetFromJsonAsync<SalesRouteDetail>($"/api/commerce/v1/routes/{created.RouteId:D}");
        Assert.NotNull(detail);
        Assert.Equal([1,2], detail.Stops.Select(stop => stop.Sequence));
        var reversed = detail.Stops.Reverse().Select(stop => stop.RouteStopId).ToArray();
        var reorder = await client.PutAsJsonAsync($"/api/commerce/v1/routes/{created.RouteId:D}/stops/order",
            new ReorderRouteStopsRequest(reversed, detail.RowVersion));
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);
        var reordered = await client.GetFromJsonAsync<SalesRouteDetail>($"/api/commerce/v1/routes/{created.RouteId:D}");
        Assert.Equal(seed.CustomerTwoId, reordered!.Stops.First().CustomerId);
        Assert.Equal([1,2], reordered.Stops.Select(stop => stop.Sequence));

        var visitDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var stop = reordered.Stops.First();
        using (var incomplete = await client.PutAsJsonAsync($"/api/commerce/v1/routes/{created.RouteId:D}/visits",
                   new RecordSalesRouteVisitRequest(stop.RouteStopId, visitDate, "Skipped", "Cliente cerrado", null,
                       DateTimeOffset.UtcNow, $"visit-{Guid.NewGuid():N}")))
            Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);

        const string observation = "Se visitó el establecimiento y estaba cerrado.";
        using (var recorded = await client.PutAsJsonAsync($"/api/commerce/v1/routes/{created.RouteId:D}/visits",
                   new RecordSalesRouteVisitRequest(stop.RouteStopId, visitDate, "Skipped", "Cliente cerrado", null,
                       DateTimeOffset.UtcNow, $"visit-{Guid.NewGuid():N}", observation)))
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);

        var visits = await client.GetFromJsonAsync<SalesRouteVisit[]>(
            $"/api/commerce/v1/routes/{created.RouteId:D}/visits?date={visitDate:yyyy-MM-dd}");
        var skipped = Assert.Single(visits!);
        Assert.Equal("Cliente cerrado", skipped.SkipReason);
        Assert.Equal(observation, skipped.VisitObservation);

        var page = await client.GetFromJsonAsync<SalesRoutePage>(
            "/api/commerce/v1/routes?page=1&pageSize=20&search=prueba&dayOfWeek=1&isActive=true");
        var listed = Assert.Single(page!.Items.Where(item => item.RouteId == created.RouteId));
        Assert.Equal("Ready", listed.PreparationStatus);
        Assert.Equal(2, listed.StopCount);
        Assert.Contains(1, listed.Days);
    }

    [Fact]
    public async Task Permissions_scope_overlap_run_order_and_row_version_are_enforced_by_server_and_sql()
    {
        var seed = await SeedCommercialPartiesAsync();
        using (var denied = fixture.CreateAdminClient())
        using (var response = await denied.GetAsync("/api/commerce/v1/routes?page=1&pageSize=20"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var client = fixture.CreateAdminClient(RoutePermissionCodes.Read, RoutePermissionCodes.Create,
            RoutePermissionCodes.Update, RoutePermissionCodes.ManageStops, RoutePermissionCodes.ManageZones,
            RoutePermissionCodes.ReadAll);
        var first = await CreateRouteAsync(client, seed.SellerId, "Ruta uno", 1, 1);
        var add = await client.PostAsJsonAsync($"/api/commerce/v1/routes/{first.RouteId:D}/stops",
            new AddRouteStopsRequest([new(seed.CustomerOneId,seed.SiteOneId,null)],first.RowVersion));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        using (var duplicateOrder = await client.PostAsJsonAsync("/api/commerce/v1/routes",
                   new CreateSalesRouteRequest(fixture.BusinessId,$"RT-{Guid.NewGuid():N}"[..12],"Ruta duplicada",
                       seed.SellerId,null,null,[new(1,1,null)])))
            Assert.Equal(HttpStatusCode.Conflict, duplicateOrder.StatusCode);

        var second = await CreateRouteAsync(client, seed.SellerId, "Ruta dos", 1, 2);
        using (var overlap = await client.PostAsJsonAsync($"/api/commerce/v1/routes/{second.RouteId:D}/stops",
                   new AddRouteStopsRequest([new(seed.CustomerOneId,seed.SiteOneId,null)],second.RowVersion)))
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

        var current = await client.GetFromJsonAsync<SalesRouteDetail>($"/api/commerce/v1/routes/{first.RouteId:D}");
        Assert.NotNull(current);
        var update = new UpdateSalesRouteRequest(current.Code,current.Name+" actualizada",current.SellerId,current.ZoneId,
            current.Notes,current.Schedules.Select(schedule=>new RouteScheduleInput(schedule.DayOfWeek,schedule.RunOrder,schedule.PlannedStartTime)).ToArray(),current.RowVersion);
        using var accepted = await client.PutAsJsonAsync($"/api/commerce/v1/routes/{first.RouteId:D}",update);
        Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);
        using var stale = await client.PutAsJsonAsync($"/api/commerce/v1/routes/{first.RouteId:D}",update);
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
    }

    private async Task<RouteMutationResult> CreateRouteAsync(HttpClient client, Guid sellerId, string name, int day, int runOrder)
    {
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/routes",
            new CreateSalesRouteRequest(fixture.BusinessId,$"RT-{Guid.NewGuid():N}"[..12],name,sellerId,null,null,[new(day,runOrder,null)]));
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        return await response.Content.ReadFromJsonAsync<RouteMutationResult>() ?? throw new InvalidOperationException("Route response missing.");
    }

    private async Task<RouteSeed> SeedCommercialPartiesAsync()
    {
        var sellerParty=Guid.NewGuid();var seller=Guid.NewGuid();var partyOne=Guid.NewGuid();var customerOne=Guid.NewGuid();var siteOne=Guid.NewGuid();var partyTwo=Guid.NewGuid();var customerTwo=Guid.NewGuid();var siteTwo=Guid.NewGuid();var searchTerm=$"RUTA{Guid.NewGuid():N}"[..16];
        const string sql="""
            DECLARE @CountryId uniqueidentifier=(SELECT TOP(1) CountryId FROM dbo.Countries WHERE IsActive=1 ORDER BY Code);
            DECLARE @DivisionId uniqueidentifier=(SELECT TOP(1) AdministrativeDivisionId FROM dbo.AdministrativeDivisions WHERE CountryId=@CountryId AND IsActive=1 ORDER BY Code);
            DECLARE @CityId uniqueidentifier=(SELECT TOP(1) CityId FROM dbo.Cities WHERE AdministrativeDivisionId=@DivisionId AND IsActive=1 ORDER BY Code);
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@SellerParty,@TenantId,N'NaturalPerson',N'Vendedor rutas',N'Complete',1,@UserId,SYSDATETIMEOFFSET()),
                  (@PartyOne,@TenantId,N'Organization',@CustomerOneName,N'Complete',1,@UserId,SYSDATETIMEOFFSET()),
                  (@PartyTwo,@TenantId,N'Organization',@CustomerTwoName,N'Complete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.CommerceSellers(SellerId,BusinessId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
            VALUES(@SellerId,@BusinessId,@SellerParty,@SellerCode,N'SaleAfterTax',N'Sale',1,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerOne,@PartyOne,@BusinessId,1,@UserId,SYSDATETIMEOFFSET()),(@CustomerTwo,@PartyTwo,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,Neighborhood,Phone,IsPrimary,IsActive,CreatedBy,CreatedAt)
            VALUES(@SiteOne,@PartyOne,N'PRINCIPAL',N'Tienda centro',@CountryId,@DivisionId,@CityId,N'Calle 1 # 2-3',N'Centro',N'3000000001',1,1,@UserId,SYSDATETIMEOFFSET()),
                  (@SiteTwo,@PartyTwo,N'PRINCIPAL',N'Tienda norte',@CountryId,@DivisionId,@CityId,N'Carrera 4 # 5-6',N'Norte',N'3000000002',1,1,@UserId,SYSDATETIMEOFFSET());
            """;
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();await using var command=new SqlCommand(sql,connection);
        command.Parameters.AddWithValue("@CustomerOneName",$"{searchTerm} uno");command.Parameters.AddWithValue("@CustomerTwoName",$"{searchTerm} dos");command.Parameters.AddWithValue("@SellerParty",sellerParty);command.Parameters.AddWithValue("@SellerId",seller);command.Parameters.AddWithValue("@SellerCode",$"V-{seller:N}"[..12]);command.Parameters.AddWithValue("@PartyOne",partyOne);command.Parameters.AddWithValue("@CustomerOne",customerOne);command.Parameters.AddWithValue("@SiteOne",siteOne);command.Parameters.AddWithValue("@PartyTwo",partyTwo);command.Parameters.AddWithValue("@CustomerTwo",customerTwo);command.Parameters.AddWithValue("@SiteTwo",siteTwo);command.Parameters.AddWithValue("@TenantId",fixture.TenantId);command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);command.Parameters.AddWithValue("@UserId",fixture.UserId);await command.ExecuteNonQueryAsync();
        return new(seller,customerOne,siteOne,customerTwo,siteTwo,searchTerm);
    }

    private sealed record RouteSeed(Guid SellerId,Guid CustomerOneId,Guid SiteOneId,Guid CustomerTwoId,Guid SiteTwoId,string SearchTerm);
}
