using System.Data;
using System.Net.Http.Json;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class WorkSessionClosureLocalDatesTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData("America/Bogota","2026-08-01","2026-08-01T04:59:59+00:00",false)]
    [InlineData("America/Bogota","2026-08-01","2026-08-02T02:30:00+00:00",true)]
    [InlineData("America/Bogota","2026-08-01","2026-08-02T05:00:00+00:00",false)]
    [InlineData("Europe/Berlin","2026-03-29","2026-03-28T23:30:00+00:00",true)]
    [InlineData("Europe/Berlin","2026-03-29","2026-03-29T22:00:00+00:00",false)]
    public async Task Closure_dates_use_each_business_calendar_including_midnight_and_daylight_saving(
        string zone,string calendarDate,string closedAt,bool included)
    {
        var closureId=await SeedClosureAsync(zone,DateTimeOffset.Parse(closedAt));
        using var client=fixture.CreateAdminClient(WorkSessionPermissionCodes.ReadCashDifferences);
        var page=await client.GetFromJsonAsync<WorkSessionClosurePage>(
            $"/api/commerce/v1/work-sessions/closures?from={calendarDate}&to={calendarDate}&page=1&pageSize=100");
        Assert.NotNull(page);
        Assert.Equal(included,page.Items.Any(item=>item.WorkSessionClosureId==closureId));
    }

    [Fact]
    public async Task Mixed_business_time_zones_are_filtered_before_paging_with_bounded_reads()
    {
        var at=new DateTimeOffset(2026,6,15,2,30,0,TimeSpan.Zero);
        var bogota=await SeedClosureAsync("America/Bogota",at);
        var berlin=await SeedClosureAsync("Europe/Berlin",at);
        using var services=fixture.CreateScope();
        var service=services.ServiceProvider.GetRequiredService<WorkSessionService>();
        var identity=new WorkSessionIdentity(fixture.UserId,fixture.TenantId,
            new HashSet<string>{WorkSessionPermissionCodes.ReadCashDifferences},fixture.BusinessId);
        var database=new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        foreach(var (date,expected,excluded) in new[] {
            (new DateOnly(2026,6,14),bogota,berlin),(new DateOnly(2026,6,15),berlin,bogota) })
        {
            using var commands=new InventoryBalanceProcessingTests.CommandCounter(database);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var page=await service.ListClosuresAsync(identity,date,date,null,1,1);
            watch.Stop();
            Assert.Equal(1,page.TotalItems);
            Assert.Equal(expected,Assert.Single(page.Items).WorkSessionClosureId);
            Assert.DoesNotContain(page.Items,item=>item.WorkSessionClosureId==excluded);
            Assert.Equal(4,commands.Count);
            Assert.True(watch.Elapsed<TimeSpan.FromSeconds(2),$"Closure first page: {watch.Elapsed.TotalMilliseconds:F1} ms.");
            var next=await service.ListClosuresAsync(identity,date,date,null,2,1);
            Assert.Empty(next.Items);
            Assert.Equal(1,next.TotalItems);
            Assert.Equal(7,commands.Count);
        }
    }

    private async Task<Guid> SeedClosureAsync(string zone,DateTimeOffset closedAt)
    {
        var businessId=Guid.NewGuid(); var closureId=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        // Isolated list-query evidence; no operational/fiscal/financial replay.
        await using var command=new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,TimeZone,IsActive,CreatedAt)
              VALUES(@Business,@Tenant,N'Sede calendario de prueba',N'Calendario de cierres',N'Prueba',N'',N'',N'',@Zone,1,SYSDATETIMEOFFSET());
            DECLARE @Session uniqueidentifier=NEWID();
            INSERT dbo.WorkSessions(WorkSessionId,TenantId,BusinessId,UserId,OpenedAt,LastActivityAt,ClosedAt,Status)
              VALUES(@Session,@Tenant,@Business,@User,DATEADD(hour,-1,@ClosedAt),@ClosedAt,@ClosedAt,N'Closed');
            INSERT dbo.WorkSessionClosures(WorkSessionClosureId,WorkSessionId,ClosedByUserId,IdempotencyKey,
              TotalSales,TotalRefunds,TotalOther,NetAmount,ExpectedCash,CountedCash,CashDifference,
              ReceiptSnapshotJson,ReceiptHash,ClosedAt)
              VALUES(@Closure,@Session,@User,CONVERT(nvarchar(36),@Closure),0,0,0,0,0,0,0,N'{}',0x00,@ClosedAt);
            """,connection);
        command.Parameters.AddWithValue("@Business",businessId); command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        command.Parameters.AddWithValue("@User",fixture.UserId); command.Parameters.AddWithValue("@Closure",closureId);
        command.Parameters.AddWithValue("@Zone",zone); command.Parameters.Add("@ClosedAt",SqlDbType.DateTimeOffset).Value=closedAt;
        await command.ExecuteNonQueryAsync();
        return closureId;
    }
}
