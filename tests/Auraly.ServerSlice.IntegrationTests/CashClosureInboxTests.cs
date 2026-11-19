using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class CashClosureInboxTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Inbox_pages_both_origins_before_details_and_keeps_their_permissions_and_identity()
    {
        var at = DateTimeOffset.Parse("2030-02-12T05:00:00Z");
        var marker = Guid.NewGuid().ToString("N");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            DECLARE @Items TABLE(Id uniqueidentifier,SessionId uniqueidentifier,DispatchId uniqueidentifier,N int);
            INSERT @Items SELECT TOP(200) NEWID(),NEWID(),NEWID(),ROW_NUMBER() OVER(ORDER BY object_id) FROM sys.all_objects;
            INSERT dbo.WorkSessions(WorkSessionId,TenantId,BusinessId,UserId,OpenedAt,LastActivityAt,ClosedAt,Status)
              SELECT SessionId,@Tenant,@Business,@User,DATEADD(hour,-1,@At),@At,@At,N'Closed' FROM @Items;
            INSERT dbo.WorkSessionClosures(WorkSessionClosureId,WorkSessionId,ClosedByUserId,IdempotencyKey,
              TotalSales,TotalRefunds,TotalOther,NetAmount,ExpectedCash,CountedCash,CashDifference,
              ReceiptSnapshotJson,ReceiptHash,ClosedAt)
              SELECT Id,SessionId,@User,CONVERT(nvarchar(36),Id),0,0,0,0,0,0,0,N'{}',0x00,@At FROM @Items;
            INSERT dbo.Dispatches(DispatchId,TenantId,BusinessId,DispatchNumber,ScheduledDate,
              DriverName,DriverUserId,Status,CreatedBy,UpdatedBy,CreatedAt,UpdatedAt)
              SELECT DispatchId,@Tenant,@Business,CONCAT(@Marker,N'-',N),CONVERT(date,@At),N'Transportador de prueba',
                @User,N'PendingSettlement',@User,@User,@At,@At FROM @Items;
            INSERT dbo.WorkSessions(WorkSessionId,TenantId,BusinessId,UserId,Kind,DispatchId,OpenedAt,LastActivityAt,ClosedAt,Status)
              SELECT NEWID(),@Tenant,@Business,@User,N'Dispatch',DispatchId,@At,@At,@At,N'Closed' FROM @Items;
            INSERT dbo.WorkSessionClosures(WorkSessionClosureId,WorkSessionId,ClosedByUserId,IdempotencyKey,
              TotalSales,TotalRefunds,TotalOther,NetAmount,ExpectedCash,CountedCash,CashDifference,Note,
              ReceiptSnapshotJson,ReceiptHash,ClosedAt,DispatchStatus,DispatchExpectedCash,
              DispatchTransferTotal,DispatchCreditDocumentTotal,DispatchCreditAdvanceTotal,DispatchReturnTotal,ReconciliationStatus)
              SELECT NEWID(),session.WorkSessionId,@User,CONVERT(nvarchar(36),item.DispatchId),100,0,0,100,100,90,-10,
                N'Faltante de prueba',N'{}',0x00,DATEADD(second,CASE WHEN item.N%2=0 THEN 1 ELSE -1 END,@At),
                CASE WHEN item.N=1 THEN N'Completed' ELSE N'PendingReview' END,100,0,0,0,0,
                CASE WHEN item.N=1 THEN N'ReconciledWithDifferences' ELSE N'Pending' END
              FROM @Items item JOIN dbo.WorkSessions session ON session.DispatchId=item.DispatchId;
            """, connection);
        seed.Parameters.AddWithValue("@At", at); seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        seed.Parameters.AddWithValue("@Business", fixture.BusinessId); seed.Parameters.AddWithValue("@User", fixture.UserId);
        seed.Parameters.AddWithValue("@Marker", marker);
        await seed.ExecuteNonQueryAsync();
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WorkSessionService>();
        var identity = new WorkSessionIdentity(fixture.UserId, fixture.TenantId,
            new HashSet<string> { WorkSessionPermissionCodes.ReadCashDifferences, "dispatches.settle" }, fixture.BusinessId);
        using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
        var watch = Stopwatch.StartNew();
        var first = await service.ListClosureInboxAsync(identity, new(2030,2,12), new(2030,2,12), null, 1, 50);
        watch.Stop();
        Assert.Equal(300, first.TotalItems); // 200 cash + 100 dispatch after the local midnight boundary.
        Assert.Equal(50, first.Items.Count);
        Assert.All(first.Items, row => { Assert.Equal("Dispatch", row.Type); Assert.Null(row.CashClosure);
            Assert.NotNull(row.DispatchClosure); Assert.NotEqual(row.ClosureId, row.DispatchClosure.DispatchId); });
        Assert.Equal(2, counter.Count); // One zone lookup and one count/page batch; no cash snapshots on this page.
        var second = await service.ListClosureInboxAsync(identity, new(2030,2,12), new(2030,2,12), null, 3, 50);
        Assert.All(second.Items, row => { Assert.Equal("Cash", row.Type); Assert.NotNull(row.CashClosure); Assert.Null(row.DispatchClosure); });
        Assert.Equal(5, counter.Count);
        Assert.Empty(first.Items.Select(item => item.ClosureId).Intersect(second.Items.Select(item => item.ClosureId)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        output.WriteLine($"400 headers, page 50, first page {watch.Elapsed.TotalMilliseconds:F2} ms; 2 trips (dispatch), 3 (cash).");

        using var cashReader = fixture.CreateAdminClient(WorkSessionPermissionCodes.ReadCashDifferences);
        var cashOnly = await cashReader.GetFromJsonAsync<CashClosureInboxPage>(
            "/api/commerce/v1/work-sessions/closures/inbox?from=2030-02-12&to=2030-02-12&page=1&pageSize=50");
        Assert.Equal(200, cashOnly!.TotalItems); Assert.All(cashOnly.Items, row => Assert.Equal("Cash", row.Type));
        var dispatchOnly = await service.ListClosureInboxAsync(identity with { Permissions = new HashSet<string> { "dispatches.settle" } },
            new(2030,2,12), new(2030,2,12), null, 1, 50);
        Assert.Equal(100, dispatchOnly.TotalItems); Assert.All(dispatchOnly.Items, row => Assert.Equal("Dispatch", row.Type));
        var foreign = await service.ListClosureInboxAsync(identity with { TenantId = Guid.NewGuid() }, new(2030,2,12), new(2030,2,12), null, 1, 50);
        Assert.Empty(foreign.Items);
        var otherBusiness = await service.ListClosureInboxAsync(identity with { BusinessId = Guid.NewGuid(), Permissions = new HashSet<string> { "dispatches.settle" } },
            new(2030,2,12), new(2030,2,12), null, 1, 50);
        Assert.Empty(otherBusiness.Items);
        using var denied = fixture.CreateAdminClient("dispatches.read");
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(
            "/api/commerce/v1/work-sessions/closures/inbox?from=2030-02-12&to=2030-02-12")).StatusCode);
        var legacy = await cashReader.GetFromJsonAsync<WorkSessionClosurePage>(
            "/api/commerce/v1/work-sessions/closures?from=2030-02-12&to=2030-02-12&page=1&pageSize=50");
        Assert.Equal(200, legacy!.TotalItems);
        Assert.All(legacy.Items, item => Assert.NotEqual(Guid.Empty, item.WorkSessionId));
    }
}
