using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name,DisableParallelization=true)]
public sealed class SalesReportPerformanceCollection:ICollectionFixture<ServerSliceFixture>
{
    public const string Name="Commercial reporting volume";
}

[Collection(SalesReportPerformanceCollection.Name)]
public sealed class SalesReportPerformanceTests(ServerSliceFixture fixture,ITestOutputHelper output)
{
    [Fact]
    [Trait("Performance","CommercialReporting")]
    public async Task One_million_projected_lines_keep_two_commands_and_bounded_first_and_following_pages()
    {
        var business=Guid.NewGuid();var warehouse=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using(var seed=new SqlCommand("""
            SET NOCOUNT ON;
            -- Only this disposable benchmark database: bulk preparation is outside the measured read path.
            ALTER DATABASE CURRENT SET RECOVERY SIMPLE;
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,TimeZone,PointOfSaleMode)
            VALUES(@Business,@Tenant,N'Volumen reporting',N'',N'',N'',N'',N'',N'UTC',N'RestaurantBar');
            WITH digits AS(SELECT value FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(value)),
            numbers AS(SELECT 1+a.value+10*b.value+100*c.value+1000*d.value+10000*e.value Seq
              FROM digits a CROSS JOIN digits b CROSS JOIN digits c CROSS JOIN digits d CROSS JOIN digits e)
            SELECT Seq,NEWID() Id,DATEADD(day,Seq%730,CONVERT(date,'20241105')) Date,
              CONVERT(uniqueidentifier,CONVERT(binary(16),HASHBYTES('SHA2_256',CONCAT(N'customer-',Seq%2000)))) Customer
            INTO #Documents FROM numbers;
            CREATE UNIQUE CLUSTERED INDEX IX_SeedDocuments ON #Documents(Seq);
            INSERT reporting.SalesReportDocuments(DocumentId,TenantId,BusinessId,DocumentType,DocumentNumber,IssuedAt,
              BusinessLocalDate,TimeZoneId,WarehouseId,WarehouseName,SellerName,CustomerId,CustomerIdentification,CustomerName,SourceMode,
              GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,RoundingAdjustmentAmount,CreditAmount,CollectedAmount,
              RecognizedCostAmount,ProjectionVersion,SourcePayloadHash,ProjectedAt,ProductQuantity)
            SELECT Id,@Tenant,@Business,N'SalesInvoice',CONCAT(N'PERF-',Seq),CONVERT(datetimeoffset,Date),Date,N'UTC',@Warehouse,
              N'Bodega',N'Sin vendedor',Customer,CONCAT(N'ID-',Seq%2000),CONCAT(N'Cliente ',Seq%2000),N'Online',
              1000,0,1000,190,1200,10,0,1200,600,5,HASHBYTES('SHA2_256',N'performance fixture'),SYSDATETIMEOFFSET(),10 FROM #Documents;
            RAISERROR(N'Benchmark seed: documents complete',10,1) WITH NOWAIT;
            INSERT reporting.SalesReportPaymentFacts(SourceDocumentId,SourceDocumentType,PaymentNumber,TenantId,BusinessId,
              BusinessLocalDate,MovementType,MethodCode,Amount,ProjectedAt)
            SELECT Id,N'SalesInvoice',1,@Tenant,@Business,Date,N'Payment',N'Cash',1200,SYSDATETIMEOFFSET() FROM #Documents;
            -- Reuse the same 1,000 deterministic product identities instead of hashing them four million times.
            SELECT Seq%1000 Slot,
              CONVERT(uniqueidentifier,CONVERT(binary(16),HASHBYTES('SHA2_256',CONCAT(N'product-',Seq%1000)))) ProductId,
              CONCAT(N'P-',Seq%1000) ProductCode,CONCAT(N'Producto ',Seq%1000) ProductName,
              CONVERT(uniqueidentifier,CONVERT(binary(16),HASHBYTES('SHA2_256',CONCAT(N'category-',Seq%20)))) CategoryId,
              CONCAT(N'Categoría ',Seq%20) CategoryName,
              CONVERT(uniqueidentifier,CONVERT(binary(16),HASHBYTES('SHA2_256',CONCAT(N'supplier-',Seq%10)))) SupplierId,
              CONCAT(N'Proveedor ',Seq%10) SupplierName INTO #SeedProducts FROM #Documents WHERE Seq<=1000;
            CREATE UNIQUE CLUSTERED INDEX IX_SeedProducts ON #SeedProducts(Slot);
            SELECT Slot,CONVERT(uniqueidentifier,CONVERT(binary(16),HASHBYTES('SHA2_256',CONCAT(N'waiter-',Slot)))) WaiterId,
              CONCAT(N'Mesero ',Slot) WaiterName INTO #SeedWaiters FROM(VALUES(0),(1),(2),(3),(4)) waiter(Slot);

            DECLARE @BatchStart int=1;
            WHILE @BatchStart<=100000
            BEGIN
            INSERT reporting.SalesReportLineFacts WITH(TABLOCK)(FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
              OriginalSaleDocumentId,OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,WarehouseId,CustomerId,ProductId,
              ProductCode,ProductName,CategoryId,CategoryName,SupplierId,SupplierName,WaiterPartyId,WaiterName,UnitCode,TaxCode,TaxRate,
              Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,RecognizedCostAmount,CostAvailable,ProjectionVersion,ProjectedAt)
            -- Append fact identifiers in batch order, like the canonical sequential ID generator;
            -- avoid random clustered-page splits while preserving all one million distinct lines.
            SELECT CONVERT(uniqueidentifier,SUBSTRING(CONVERT(binary(16),@Business),1,10)+CONVERT(binary(6),CONVERT(bigint,d.Seq)*16+line.Number)),
              @Tenant,@Business,d.Id,N'SalesInvoice',line.Number,d.Id,line.Number,N'Sale',CONVERT(datetimeoffset,d.Date),d.Date,
              @Warehouse,d.Customer,p.ProductId,p.ProductCode,p.ProductName,p.CategoryId,p.CategoryName,
              p.SupplierId,p.SupplierName,w.WaiterId,w.WaiterName,N'EA',N'IVA',19,1,100,0,100,19,119,60,1,5,SYSDATETIMEOFFSET()
            FROM #Documents d CROSS JOIN(VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) line(Number)
            JOIN #SeedProducts p ON p.Slot=(d.Seq+line.Number)%1000
            JOIN #SeedWaiters w ON w.Slot=line.Number%5
            WHERE d.Seq BETWEEN @BatchStart AND @BatchStart+4999 OPTION(MAXDOP 1);
            SET @BatchStart+=5000;
            END;
            RAISERROR(N'Benchmark seed: one million lines complete',10,1) WITH NOWAIT;
            INSERT reporting.SalesReportTaxFacts(SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,TaxCode,TaxRate,
              TenantId,BusinessId,BusinessLocalDate,TaxableAmount,TaxAmount,TotalAmount,ProjectedAt,OccurredAt)
            SELECT Id,N'SalesInvoice',Id,N'IVA',19,@Tenant,@Business,Date,1000,190,1190,SYSDATETIMEOFFSET(),CONVERT(datetimeoffset,Date) FROM #Documents;
            INSERT reporting.SalesReportDailyTotals(BusinessId,BusinessLocalDate,CurrencyCode,DocumentCount,UnitsSold,UnitsReturned,
              GrossSales,Discounts,Returns,NetUntaxedSales,NetTax,NetTotalSales,NetRecognizedCost,GrossProfit,CreditSales,Collected,Refunded,ProjectionVersion,UpdatedAt)
            SELECT @Business,Date,N'COP',COUNT_BIG(*),COUNT_BIG(*)*10,0,COUNT_BIG(*)*1000,0,0,COUNT_BIG(*)*1000,
              COUNT_BIG(*)*190,COUNT_BIG(*)*1200,COUNT_BIG(*)*600,COUNT_BIG(*)*400,0,COUNT_BIG(*)*1200,0,5,SYSDATETIMEOFFSET()
            FROM #Documents GROUP BY Date;
            SELECT Id OriginalId,NEWID() Id,CASE WHEN Date<'20261104' THEN DATEADD(day,1,Date) ELSE Date END Date
            INTO #Returns FROM #Documents WHERE Seq%20=0;
            INSERT reporting.SalesReportLineFacts(FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
              OriginalSaleDocumentId,OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,WarehouseId,CustomerId,ProductId,
              ProductCode,ProductName,CategoryId,CategoryName,SupplierId,SupplierName,WaiterPartyId,WaiterName,UnitCode,TaxCode,TaxRate,
              Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,RecognizedCostAmount,CostAvailable,ProjectionVersion,ProjectedAt)
            SELECT NEWID(),@Tenant,@Business,r.Id,N'SalesReturn',1,r.OriginalId,1,N'Return',CONVERT(datetimeoffset,r.Date),r.Date,
              f.WarehouseId,f.CustomerId,f.ProductId,f.ProductCode,f.ProductName,f.CategoryId,f.CategoryName,f.SupplierId,f.SupplierName,
              f.WaiterPartyId,f.WaiterName,f.UnitCode,f.TaxCode,f.TaxRate,-1,-100,0,-100,-19,-119,-60,1,5,SYSDATETIMEOFFSET()
            FROM #Returns r JOIN reporting.SalesReportLineFacts f ON f.SourceDocumentId=r.OriginalId AND f.SourceDocumentType=N'SalesInvoice' AND f.SourceLineNumber=1;
            INSERT reporting.SalesReportTaxFacts(SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,TaxCode,TaxRate,
              TenantId,BusinessId,BusinessLocalDate,TaxableAmount,TaxAmount,TotalAmount,ProjectedAt,OccurredAt)
            SELECT Id,N'SalesReturn',OriginalId,N'IVA',19,@Tenant,@Business,Date,-100,-19,-119,SYSDATETIMEOFFSET(),CONVERT(datetimeoffset,Date) FROM #Returns;
            INSERT reporting.SalesReportPaymentFacts(SourceDocumentId,SourceDocumentType,PaymentNumber,TenantId,BusinessId,BusinessLocalDate,MovementType,MethodCode,Amount,ProjectedAt)
            SELECT Id,N'SalesReturn',1,@Tenant,@Business,Date,N'Refund',N'Cash',-100,SYSDATETIMEOFFSET() FROM #Returns;
            UPDATE daily SET UnitsReturned=returned.Count,Returns=returned.Count*100,NetUntaxedSales=NetUntaxedSales-returned.Count*100,
              NetTax=NetTax-returned.Count*19,NetTotalSales=NetTotalSales-returned.Count*100,NetRecognizedCost=NetRecognizedCost-returned.Count*60,
              GrossProfit=GrossProfit-returned.Count*40,Refunded=returned.Count*100
            FROM reporting.SalesReportDailyTotals daily JOIN(SELECT Date,COUNT(*) Count FROM #Returns GROUP BY Date) returned
              ON returned.Date=daily.BusinessLocalDate WHERE daily.BusinessId=@Business;
            INSERT reporting.SalesReportingCheckpoints(BusinessId,ProjectionVersion,LastSourceDocumentId,LastSourceDocumentType,LastProjectedAt)
            SELECT TOP(1) @Business,5,Id,N'SalesInvoice',SYSDATETIMEOFFSET() FROM #Documents;
            RAISERROR(N'Benchmark seed: returns and daily totals complete',10,1) WITH NOWAIT;
            UPDATE STATISTICS reporting.SalesReportDocuments WITH FULLSCAN;
            UPDATE STATISTICS reporting.SalesReportLineFacts WITH FULLSCAN;
            UPDATE STATISTICS reporting.SalesReportTaxFacts WITH FULLSCAN;
            """,connection))
        {
            connection.InfoMessage+=(_,message)=>output.WriteLine(message.Message);
            seed.Parameters.AddWithValue("@Business",business);seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            seed.Parameters.AddWithValue("@Warehouse",warehouse);seed.CommandTimeout=600;await seed.ExecuteNonQueryAsync();
        }
        using var scope=fixture.CreateScope();var reports=scope.ServiceProvider.GetRequiredService<SalesReportingService>();
        var user=new SalesReportingUserIdentity(fixture.UserId,fixture.TenantId,business,
            new HashSet<string>{SalesReportingPermissionCodes.Read,SalesReportingPermissionCodes.ReadAll});
        var month=new SalesReportFilter(new(2026,10,5),new(2026,11,4));
        var first=await reports.ExploreAsync(user,new(month));Assert.Equal(25,first.Items.Count);Assert.True(first.TotalCount>4000);
        var next=await reports.ExploreAsync(user,new(month,Page:2));
        Assert.Equal(first.Totals,next.Totals);Assert.DoesNotContain(next.Items,row=>first.Items.Any(other=>other.Key==row.Key));
        var supplier=await reports.ExploreAsync(user,new(month,GroupBy:"supplier"));var supplierId=Guid.Parse(supplier.Items[0].Key);
        var failures=new List<string>();
        var failedPlans=new List<(string Name,SqlCommand Command)>();
        await Measure("Resumen diario",async()=>{await reports.GetTodayAsync(user);},300);
        var summary=await reports.GetSummaryAsync(user,month,null,null,includeBreakdowns:true);
        Assert.NotNull(summary.Breakdowns);
        Assert.Equal((await reports.GetSummaryAsync(user,month,null,null)).Current,summary.Current);
        await Measure("Resumen con todos sus paneles",async()=>{await reports.GetSummaryAsync(user,month,null,null,includeBreakdowns:true);},1500);
        await Measure("Impacto comercial",async()=>{await reports.ExploreAsync(user,new(month,Module:"impact",GroupBy:"customer"));},1500);
        await Measure("Proveedores compras y ventas",async()=>{await reports.ExploreAsync(user,new(month,Module:"supplier",GroupBy:"supplier"));},1500);
        await Measure("Comprobantes primera página",async()=>{await reports.ExploreAsync(user,new(month));},600);
        await Measure("Comprobantes segunda página",async()=>{await reports.ExploreAsync(user,new(month,Page:2));},600);
        await Measure("Proveedor + bodega primera página",async()=>{await reports.ExploreAsync(user,new(month with {SupplierId=supplierId,WarehouseId=warehouse}));},1500);
        await Measure("Proveedor + bodega segunda página",async()=>{await reports.ExploreAsync(user,new(month with {SupplierId=supplierId,WarehouseId=warehouse},Page:2));},1500);
        var multiple=month with {SupplierIds=[supplierId],WarehouseIds=[warehouse]};
        Assert.Equal((await reports.ExploreAsync(user,new(month with {SupplierId=supplierId,WarehouseId=warehouse}))).Totals,
            (await reports.ExploreAsync(user,new(multiple))).Totals);
        await Measure("Selección múltiple proveedor + bodega primera página",async()=>{await reports.ExploreAsync(user,new(multiple));},1500);
        await Measure("Selección múltiple proveedor + bodega segunda página",async()=>{await reports.ExploreAsync(user,new(multiple,Page:2));},1500);
        var profitability=new SalesReportExplorerRequest(month,GroupBy:"product",Module:"products",Products:new(MarginFrom:0m,ReturnActivity:"with-returns"));
        var portfolio=await reports.ExploreAsync(user,profitability);
        Assert.True(portfolio.Products!.Products>0);Assert.Equal(portfolio.Products,(await reports.ExploreAsync(user,profitability with {Page=2})).Products);
        await Measure("Rentabilidad y devoluciones primera página",async()=>{await reports.ExploreAsync(user,profitability);},1500);
        await Measure("Rentabilidad y devoluciones segunda página",async()=>{await reports.ExploreAsync(user,profitability with {Page=2});},1500);
        await Measure("Rentabilidad con comparación",async()=>{await reports.ExploreAsync(user,profitability with {Comparison="previous-period"});},1500);
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var concurrent=await Task.WhenAll(reports.ExploreAsync(user,new(month)),
                reports.ExploreAsync(user,new(month,Page:2)),reports.ExploreAsync(user,profitability));
            Assert.Equal(6,counter.Count);
            Assert.Equal(first.Totals,concurrent[0].Totals);Assert.Equal(first.Totals,concurrent[1].Totals);
            Assert.Equal(portfolio.Totals,concurrent[2].Totals);
        }
        // Capture plans only after every measured read has finished. This keeps
        // diagnostics out of the latency samples and exposes runner-specific plans.
        foreach(var (name,command) in failedPlans)
        {
            using(command)
            {
                command.Connection=connection;command.CommandTimeout=120;
                command.CommandText="SET STATISTICS XML ON;\n"+command.CommandText;
                try
                {
                    await using var plans=await command.ExecuteReaderAsync();
                    do
                    {
                        while(await plans.ReadAsync())
                            if(plans.FieldCount==1&&plans.GetName(0).Contains("Showplan",StringComparison.OrdinalIgnoreCase))
                                WritePlan(name,XDocument.Parse(plans.GetString(0)));
                    }while(await plans.NextResultAsync());
                }
                catch(SqlException error){output.WriteLine("{0}: diagnóstico SQL no disponible: {1}",name,error.Message);}
            }
        }
        Assert.True(failures.Count==0,string.Join(Environment.NewLine,failures));

        async Task Measure(string name,Func<Task> action,int budget)
        {
            SqlCommand? captured=null;
            using(var capture=new InventoryBalanceProcessingTests.CommandCounter(connection.Database,beforeExecute:command=>
            {
                if(!command.Parameters.Contains("@ReportingLockMode"))return;
                captured?.Dispose();captured=new SqlCommand(command.CommandText);
                foreach(SqlParameter parameter in command.Parameters)
                    captured.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
            }))
                for(var warmup=0;warmup<3;warmup++)await action();
            var samples=new List<double>();
            for(var iteration=0;iteration<15;iteration++)
            {
                using var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var timer=Stopwatch.StartNew();await action();timer.Stop();
                Assert.Equal(2,counter.Count);samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            samples.Sort();var p95=samples[(int)Math.Ceiling(samples.Count*.95)-1];
            output.WriteLine("{0}: p50={1:F1} ms, p95={2:F1} ms, 2 SQL, presupuesto={3} ms; 100k ventas + 5k devoluciones / 1.005m líneas / 24 meses, rango de 31 días.",name,samples[samples.Count/2],p95,budget);
            if(p95>=budget)
            {
                failures.Add($"{name}: p95 {p95:F1} ms supera {budget} ms.");
                Assert.NotNull(captured);failedPlans.Add((name,captured));
            }
            else captured?.Dispose();
        }

        void WritePlan(string name,XDocument plan)
        {
            XNamespace ns="http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            var timing=plan.Descendants(ns+"QueryTimeStats").FirstOrDefault();
            if(timing is null||Number(timing,"ElapsedTime")<50)return;
            var text=plan.Descendants(ns+"StmtSimple").FirstOrDefault()?.Attribute("StatementText")?.Value??"";
            output.WriteLine("PLAN {0}: elapsed={1} ms, cpu={2} ms: {3}",name,Number(timing,"ElapsedTime"),Number(timing,"CpuTime"),text[..Math.Min(160,text.Length)]);
            foreach(var operation in plan.Descendants(ns+"RelOp"))
            {
                var index=operation.Element(ns+"IndexScan")?.Element(ns+"Object");
                var counters=operation.Elements(ns+"RunTimeInformation").Elements(ns+"RunTimeCountersPerThread").ToArray();
                var rows=counters.Sum(value=>Number(value,"ActualRowsRead"));
                var executions=counters.Sum(value=>Number(value,"ActualExecutions"));
                if(index is not null&&(rows>=100000||executions>=10000))
                    output.WriteLine("  {0}/{1} {2}.{3}: rowsRead={4}, executions={5}, logicalReads={6}",
                        operation.Attribute("PhysicalOp")?.Value,operation.Attribute("LogicalOp")?.Value,
                        index.Attribute("Table")?.Value,index.Attribute("Index")?.Value,rows,executions,
                        counters.Sum(value=>Number(value,"ActualLogicalReads")));
            }
            var memory=plan.Descendants(ns+"MemoryGrantInfo").FirstOrDefault();
            if(memory is not null)output.WriteLine("  {0}",memory.ToString(SaveOptions.DisableFormatting));
        }
        static double Number(XElement value,string name)=>double.Parse(value.Attribute(name)?.Value??"0",CultureInfo.InvariantCulture);
    }
}
