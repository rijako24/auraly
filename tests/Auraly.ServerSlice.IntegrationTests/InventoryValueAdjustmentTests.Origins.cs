using System.Data;
using System.Diagnostics;
using Auraly.Application.DocumentProcessing;
using Auraly.Domain.Inventory;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class InventoryValueAdjustmentTests
{
    [Fact]
    public async Task Shared_prices_do_not_consume_another_business_warehouse_receipt_origins()
    {
        await using var scope=await OpenLedgerAsync(1,true);
        await ClearPoolBalancesAsync(scope);
        Guid otherBusiness; Guid otherWarehouse;
        await using(var member=new SqlCommand("SELECT BusinessId,WarehouseId FROM dbo.InventoryBalances WHERE ProductId=@Product AND BusinessId<>@Business",
            scope.Connection,scope.Transaction))
        {
            member.Parameters.AddWithValue("@Product",scope.Products[0]); member.Parameters.AddWithValue("@Business",fixture.BusinessId);
            await using var reader=await member.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); otherBusiness=reader.GetGuid(0); otherWarehouse=reader.GetGuid(1);
        }
        var otherDocument=Guid.NewGuid(); var otherJob=Guid.NewGuid(); long sequence;
        await using(var job=new SqlCommand("""
            DECLARE @Sequence bigint=(SELECT COALESCE(MAX(ProcessingSequence),0)+1 FROM dbo.DocumentProcessingJobs WHERE BusinessId=@Business);
            INSERT dbo.DocumentProcessingJobs(JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
              VALUES(@Job,@Business,@Sequence,@Document,N'GoodsReceipt',N'Pending',SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
            SELECT @Sequence;
            """,scope.Connection,scope.Transaction))
        {
            job.Parameters.AddWithValue("@Business",otherBusiness); job.Parameters.AddWithValue("@Job",otherJob);
            job.Parameters.AddWithValue("@Document",otherDocument);
            sequence=Assert.IsType<long>(await job.ExecuteScalarAsync());
        }
        DocumentProcessingContext context=new(new(fixture.TenantId),new(otherBusiness),new(otherDocument),"GoodsReceipt");
        var otherSession=new SqlDocumentProcessingSessionAccessor.Session(scope.Connection,scope.Transaction,context,otherJob,sequence);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        await writer.PostBatchAsync(otherSession,[Physical(scope,1,5) with { BusinessId=otherBusiness,
            WarehouseId=otherWarehouse,DocumentId=otherDocument }],default);
        await writer.PostBatchAsync(scope.Session,[Physical(scope,2,-3,"Sale")],default);
        Assert.Empty(await ReadCurrentOriginsAsync(scope));
        var remaining=Assert.Single(await ReadCurrentOriginsAsync(scope,true));
        Assert.Equal(otherWarehouse,remaining.WarehouseId);
        Assert.Equal(1,remaining.SourceLine); Assert.Equal(5,remaining.Quantity);
        await using var stock=new SqlCommand("SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE ProductId=@Product AND BusinessId=@Business",
            scope.Connection,scope.Transaction);
        stock.Parameters.AddWithValue("@Product",scope.Products[0]); stock.Parameters.AddWithValue("@Business",otherBusiness);
        Assert.Equal(5m,await stock.ExecuteScalarAsync());
    }

    [Fact]
    public async Task A_sale_in_another_warehouse_does_not_consume_a_receipt_without_a_transfer()
    {
        await using var scope=await OpenLedgerAsync(1,false);
        await using(var initial=new SqlCommand("""
            UPDATE dbo.InventoryBalances SET QuantityOnHand=0,InventoryValue=0 WHERE ProductId=@Product;
            UPDATE dbo.Warehouses SET AllowNegativeStockSales=1 WHERE WarehouseId=@Warehouse;
            """,scope.Connection,scope.Transaction))
        {
            initial.Parameters.AddWithValue("@Product",scope.Products[0]);
            initial.Parameters.AddWithValue("@Warehouse",fixture.WarehouseId);
            await initial.ExecuteNonQueryAsync();
        }
        await using var destination=new SqlCommand("SELECT WarehouseId FROM dbo.InventoryBalances WHERE ProductId=@Product AND WarehouseId<>@Warehouse",
            scope.Connection,scope.Transaction);
        destination.Parameters.AddWithValue("@Product",scope.Products[0]); destination.Parameters.AddWithValue("@Warehouse",fixture.WarehouseId);
        var warehouse=Assert.IsType<Guid>(await destination.ExecuteScalarAsync());
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        await writer.PostBatchAsync(scope.Session,[Physical(scope,1,5) with { WarehouseId=warehouse },Physical(scope,2,-3,"Sale")],default);
        Assert.Empty(await ReadCurrentOriginsAsync(scope));
        var position=Assert.Single(await ReadCurrentOriginsAsync(scope,true));
        Assert.Equal(warehouse,position.WarehouseId);
        Assert.Equal(1,position.SourceLine);
        Assert.Equal(5,position.Quantity);
        await using var balances=new SqlCommand("SELECT QuantityOnHand FROM dbo.InventoryBalances WHERE ProductId=@Product ORDER BY QuantityOnHand",
            scope.Connection,scope.Transaction);
        balances.Parameters.AddWithValue("@Product",scope.Products[0]);
        await using var reader=await balances.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(-3,reader.GetDecimal(0));
        Assert.True(await reader.ReadAsync()); Assert.Equal(5,reader.GetDecimal(0));
    }

    [Fact]
    public async Task Receipt_origins_consume_unknown_historical_stock_then_the_oldest_new_receipt()
    {
        await using var scope=await OpenLedgerAsync(1,false);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        var before=await ReadFinancialCountsAsync(scope);
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database))
        {
            await writer.PostBatchAsync(scope.Session,[Physical(scope,1,5),Physical(scope,2,4),
                Physical(scope,3,-7,"ProductionInput")],default);
            Assert.Equal(2,commands.Count);
        }
        var positions=await ReadCurrentOriginsAsync(scope);
        Assert.Collection(positions.OrderBy(position=>position.SourceLine),
            position=>{ Assert.Equal(1,position.SourceLine); Assert.Equal(1,position.Quantity); },
            position=>{ Assert.Equal(2,position.SourceLine); Assert.Equal(4,position.Quantity); });
        Assert.Equal(before,await ReadFinancialCountsAsync(scope));
        await using var detail=new SqlCommand("""
            SELECT SUM(origin.QuantityChange),SUM(CASE WHEN origin.SourceMovementId IS NULL THEN origin.QuantityChange ELSE 0 END)
            FROM inventory.MovementOrigins origin JOIN dbo.InventoryMovements movement
              ON movement.InventoryMovementId=origin.InventoryMovementId
            WHERE movement.DocumentId=@Id AND movement.LineNumber=3;
            """,scope.Connection,scope.Transaction);
        detail.Parameters.AddWithValue("@Id",scope.DocumentId);
        await using var reader=await detail.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(-7,reader.GetDecimal(0)); Assert.Equal(-3,reader.GetDecimal(1));
    }

    [Fact]
    public async Task Partial_returns_restore_the_actual_sale_origins_and_cannot_restore_them_twice()
    {
        await using var scope=await OpenLedgerAsync(1,false);
        await ClearOwnBalanceAsync(scope);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        await writer.PostBatchAsync(scope.Session,[Physical(scope,1,5),Physical(scope,2,-4,"Sale")],default);
        var reference=new InventoryOriginReference(scope.DocumentId,"GoodsReceipt",2,"Sale");
        await writer.PostBatchAsync(scope.Session,[Physical(scope,3,2,"SalesReturn") with { OriginReference=reference }],default);
        await writer.PostBatchAsync(scope.Session,[Physical(scope,4,1,"SalesReturn") with { OriginReference=reference }],default);
        var position=Assert.Single(await ReadCurrentOriginsAsync(scope));
        Assert.Equal(1,position.SourceLine); Assert.Equal(4,position.Quantity);
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database))
        {
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>writer.PostBatchAsync(scope.Session,
                [Physical(scope,5,2,"SalesReturn") with { OriginReference=reference }],default));
            Assert.Contains("return exceeds",error.Message,StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1,commands.Count);
        }
        Assert.Equal(position,Assert.Single(await ReadCurrentOriginsAsync(scope)));
    }

    [Fact]
    public async Task Both_transfer_legs_preserve_receipt_origins_with_an_explicit_cost_or_a_cost_dependency()
    {
        await using var scope=await OpenLedgerAsync(1,false);
        await ClearOwnBalanceAsync(scope);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        await writer.PostBatchAsync(scope.Session,[Physical(scope,1,10)],default);
        var original=Assert.Single(await ReadCurrentOriginsAsync(scope));
        await using var destination=new SqlCommand("SELECT WarehouseId FROM dbo.InventoryBalances WHERE ProductId=@Product AND WarehouseId<>@Warehouse",
            scope.Connection,scope.Transaction);
        destination.Parameters.AddWithValue("@Product",scope.Products[0]); destination.Parameters.AddWithValue("@Warehouse",fixture.WarehouseId);
        var warehouse=Assert.IsType<Guid>(await destination.ExecuteScalarAsync());
        var outgoing=Physical(scope,2,-6,"TransferOut");
        var incoming=Physical(scope,2,6,"TransferIn") with { WarehouseId=warehouse,OriginSourcePosition=0 };
        await writer.PostBatchAsync(scope.Session,[outgoing,incoming],default);
        Assert.Equal(original with { Quantity=4 },Assert.Single(await ReadCurrentOriginsAsync(scope)));
        var destinationAfterFirst=await ReadCurrentOriginsAsync(scope,true);
        Assert.Equal(7,Assert.Single(destinationAfterFirst.Where(item=>item.WarehouseId==warehouse && item.SourceLine is null)).Quantity);
        Assert.Equal(6,Assert.Single(destinationAfterFirst.Where(item=>item.WarehouseId==warehouse && item.SourceLine==1)).Quantity);
        await writer.PostBatchAsync(scope.Session,[Physical(scope,3,-2,"TransferOut"),
            Physical(scope,3,2,"TransferIn") with { WarehouseId=warehouse,SpecifiedUnitCost=null,
                UnitCostSourcePosition=0,OriginSourcePosition=0 }],default);
        Assert.Equal(original with { Quantity=2 },Assert.Single(await ReadCurrentOriginsAsync(scope)));
        var carried=Assert.Single((await ReadCurrentOriginsAsync(scope,true)).Where(item=>item.WarehouseId==warehouse && item.SourceLine==1));
        Assert.Equal(original.OriginId,carried.OriginId); Assert.Equal(8,carried.Quantity);
        await using var proof=new SqlCommand("""
            SELECT SUM(CASE WHEN movement.MovementType=N'TransferOut' THEN -portion.QuantityChange ELSE 0 END),
              SUM(CASE WHEN movement.MovementType=N'TransferIn' THEN portion.QuantityChange ELSE 0 END),
              SUM(portion.QuantityChange)
            FROM inventory.MovementOrigins portion JOIN dbo.InventoryMovements movement
              ON movement.InventoryMovementId=portion.InventoryMovementId
            WHERE movement.DocumentId=@Id AND movement.LineNumber IN(2,3) AND portion.OriginId=@OriginId;
            """,scope.Connection,scope.Transaction);
        proof.Parameters.AddWithValue("@Id",scope.DocumentId); proof.Parameters.AddWithValue("@OriginId",original.OriginId);
        await using var reader=await proof.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(8,reader.GetDecimal(0)); Assert.Equal(8,reader.GetDecimal(1)); Assert.Equal(0,reader.GetDecimal(2));
    }

    [Fact]
    public async Task Negative_stock_coverage_does_not_attribute_already_consumed_units_to_the_new_receipt()
    {
        await using var scope=await OpenLedgerAsync(1,false);
        await ClearOwnBalanceAsync(scope);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        await writer.PostBatchAsync(scope.Session,[Physical(scope,1,-6,"Sale"),Physical(scope,2,10)],default);
        var position=Assert.Single(await ReadCurrentOriginsAsync(scope));
        Assert.Equal(2,position.SourceLine); Assert.Equal(4,position.Quantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_legacy_writer_gap_starts_unknown_coverage_without_guessing_a_receipt_or_blocking_operations(bool updatedCostContext)
    {
        await using var scope=await OpenLedgerAsync(1,false);
        await ClearOwnBalanceAsync(scope);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        await writer.PostBatchAsync(scope.Session,[Physical(scope,1,5)],default);
        await using(var legacy=new SqlCommand("""
            UPDATE dbo.InventoryBalances SET LastProcessingSequence=LastProcessingSequence+1
            WHERE ProductId=@Product AND WarehouseId=@Warehouse;
            IF @UpdatedCostContext=1 UPDATE inventory.CostPoolHeads SET LastProcessingSequence=LastProcessingSequence+1
              WHERE ProductId=@Product AND BusinessId=@Business;
            """,scope.Connection,scope.Transaction))
        {
            legacy.Parameters.AddWithValue("@Product",scope.Products[0]); legacy.Parameters.AddWithValue("@Warehouse",fixture.WarehouseId);
            legacy.Parameters.AddWithValue("@UpdatedCostContext",updatedCostContext); legacy.Parameters.AddWithValue("@Business",fixture.BusinessId);
            Assert.Equal(updatedCostContext?2:1,await legacy.ExecuteNonQueryAsync());
        }
        await writer.PostBatchAsync(scope.Session,[Physical(scope,2,2),Physical(scope,3,-1,"Sale")],default);
        var positions=await ReadCurrentOriginsAsync(scope);
        Assert.Equal(4,Assert.Single(positions.Where(position=>position.SourceLine is null)).Quantity);
        Assert.Equal(2,Assert.Single(positions.Where(position=>position.SourceLine==2)).Quantity);
        Assert.DoesNotContain(positions,position=>position.SourceLine==1);
    }

    [Fact]
    public async Task Value_only_adjustments_preserve_the_existing_origin_coverage()
    {
        await using var scope=await OpenLedgerAsync(1,false);
        await ClearOwnBalanceAsync(scope);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        await writer.PostBatchAsync(scope.Session,[Physical(scope,1,5)],default);
        var before=Assert.Single(await ReadCurrentOriginsAsync(scope));
        await writer.PostBatchAsync(scope.Session,[Posting(scope,0,30)],default);
        Assert.Equal(before,Assert.Single(await ReadCurrentOriginsAsync(scope)));
        await writer.PostBatchAsync(scope.Session,[Physical(scope,2,-1,"Sale")],default);
        Assert.Equal(before with { Quantity=4 },Assert.Single(await ReadCurrentOriginsAsync(scope)));
    }

    [Fact]
    public async Task An_origin_detail_failure_rolls_back_balances_movements_and_context_together()
    {
        await using var scope=await OpenLedgerAsync(1,false);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        using(var fault=new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database,
            commandFilter:command=>command.CommandText.Contains("INSERT inventory.MovementOrigins",StringComparison.Ordinal),
            beforeExecute:command=>command.Parameters["@MovementOriginsJson"].Value="[{\"QuantityChange\":0}]"))
            await Assert.ThrowsAsync<SqlException>(()=>writer.PostBatchAsync(scope.Session,[Physical(scope,1,5)],default));
        await scope.RollbackAsync();
        await using var verify=new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id)
              +(SELECT COUNT(*) FROM inventory.OriginPositions WHERE ProductId=@Product)
              +(SELECT COUNT(*) FROM dbo.InventoryBalances WHERE ProductId=@Product);
            """,scope.Connection);
        verify.Parameters.AddWithValue("@Id",scope.DocumentId); verify.Parameters.AddWithValue("@Product",scope.Products[0]);
        Assert.Equal(0,await verify.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(500)]
    [Trait("Performance","true")]
    public async Task Physical_origin_tracking_keeps_two_commands_for_distinct_products(int count)
    {
        await using var scope=await OpenLedgerAsync(count,false);
        using var services=fixture.CreateScope();
        var writer=services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        var postings=Enumerable.Range(0,count).Select(index=>Physical(scope,index+1,5) with { ProductId=scope.Products[index] }).ToArray();
        var samples=new List<double>();
        for(var sample=0;sample<21;sample++)
        {
            await scope.Transaction.SaveAsync("OriginSample");
            var watch=Stopwatch.StartNew();
            using(var commands=new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database))
            {
                await writer.PostBatchAsync(scope.Session,postings,default);
                watch.Stop(); Assert.Equal(2,commands.Count);
            }
            if(sample>0) samples.Add(watch.Elapsed.TotalMilliseconds);
            await scope.Transaction.RollbackAsync("OriginSample");
        }
        var p95=samples.Order().ElementAt(18);
        output.WriteLine($"Origin ledger: {count} distinct products; 2 SQL commands; 20 samples; p95 {p95:F1} ms; max {samples.Max():F1} ms.");
        Assert.True(p95<2000,$"Origin ledger p95 was {p95:F1} ms.");
    }

    private InventoryLedgerPosting Physical(LedgerScope scope,int line,decimal quantity,string kind="GoodsReceipt") =>
        new(fixture.BusinessId,fixture.WarehouseId,scope.Products[0],scope.DocumentId,"GoodsReceipt",line,kind,
            quantity,quantity>0?100m:null,quantity>0?InventoryValuationMode.WeightedAverageReceipt:InventoryValuationMode.AverageCost,
            DateTimeOffset.UtcNow);

    private async Task ClearPoolBalancesAsync(LedgerScope scope)
    {
        await using var command=new SqlCommand("UPDATE dbo.InventoryBalances SET QuantityOnHand=0,InventoryValue=0 WHERE ProductId=@Product",
            scope.Connection,scope.Transaction);
        command.Parameters.AddWithValue("@Product",scope.Products[0]);
        Assert.Equal(2,await command.ExecuteNonQueryAsync());
    }

    private async Task ClearOwnBalanceAsync(LedgerScope scope)
    {
        await using var command=new SqlCommand("UPDATE dbo.InventoryBalances SET QuantityOnHand=0,InventoryValue=0 WHERE ProductId=@Product AND WarehouseId=@Warehouse",
            scope.Connection,scope.Transaction);
        command.Parameters.AddWithValue("@Product",scope.Products[0]); command.Parameters.AddWithValue("@Warehouse",fixture.WarehouseId);
        Assert.Equal(1,await command.ExecuteNonQueryAsync());
    }

    private sealed record CurrentOrigin(Guid WarehouseId,Guid OriginId,int? SourceLine,decimal Quantity);
    private async Task<IReadOnlyList<CurrentOrigin>> ReadCurrentOriginsAsync(LedgerScope scope,bool allWarehouses=false)
    {
        await using var command=new SqlCommand("""
            SELECT position.WarehouseId,position.OriginId,source.LineNumber,position.Quantity
            FROM inventory.OriginPositions position JOIN dbo.InventoryBalances balance
              ON balance.BusinessId=position.BusinessId AND balance.WarehouseId=position.WarehouseId
              AND balance.ProductId=position.ProductId AND balance.OriginContextId=position.ContextId
              AND balance.OriginSnapshotSequence=balance.LastProcessingSequence
            LEFT JOIN dbo.InventoryMovements source ON source.InventoryMovementId=position.SourceMovementId
            WHERE position.ProductId=@Product AND (@All=1 OR position.WarehouseId=@Warehouse)
            """,scope.Connection,scope.Transaction);
        command.Parameters.AddWithValue("@Product",scope.Products[0]); command.Parameters.AddWithValue("@Warehouse",fixture.WarehouseId);
        command.Parameters.AddWithValue("@All",allWarehouses);
        var result=new List<CurrentOrigin>();
        await using var reader=await command.ExecuteReaderAsync();
        while(await reader.ReadAsync()) result.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.IsDBNull(2)?null:reader.GetInt32(2),reader.GetDecimal(3)));
        return result;
    }
}
