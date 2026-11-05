using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(SalesReportingSliceCollection.Name)]
public sealed class SalesReportExplorerTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Readers_do_not_wait_for_projection_writers_and_still_respect_explicit_rebuilds()
    {
        using var scope=fixture.CreateScope();
        var reporting=scope.ServiceProvider.GetRequiredService<SalesReportingService>();
        var user=new SalesReportingUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{SalesReportingPermissionCodes.Read,SalesReportingPermissionCodes.ReadAll});
        var request=new SalesReportExplorerRequest(new(new(2026,7,27),new(2026,7,27)));
        string? readerMode=null,readerResource=null;
        var resource=SqlSalesReportingProcessor.ReportingLockResource(fixture.BusinessId);
        SalesReportExplorerPage baseline;
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,beforeExecute:command=>
            {
                if(command.Parameters.Contains("@ReportingLockMode"))
                {
                    readerMode=(string)command.Parameters["@ReportingLockMode"].Value;
                    readerResource=(string)command.Parameters["@ReportingLockResource"].Value;
                }
            }))
            baseline=await reporting.ExploreAsync(user,request);
        Assert.Equal(resource,readerResource);Assert.NotNull(readerMode);
        await using var holder=new SqlConnection(fixture.ConnectionString);
        await using var probe=new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync();await probe.OpenAsync();
        foreach(var mode in new[]{readerMode,"Shared","Exclusive"})
        {
            await using var held=(SqlTransaction)await holder.BeginTransactionAsync();
            await using var hold=new SqlCommand("""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource=@Resource,@LockMode=@Mode,@LockOwner=N'Transaction',@LockTimeout=0;
                SELECT @result;
                """,holder,held);
            hold.Parameters.AddWithValue("@Resource",resource);hold.Parameters.AddWithValue("@Mode",mode);
            Assert.True(Convert.ToInt32(await hold.ExecuteScalarAsync())>=0);
            await using var probing=(SqlTransaction)await probe.BeginTransactionAsync();
            await using var compatibility=new SqlCommand("SELECT APPLOCK_TEST(N'public',@Resource,@Mode,N'Transaction');",probe,probing);
            compatibility.Parameters.AddWithValue("@Resource",resource);compatibility.Parameters.AddWithValue("@Mode",readerMode);
            Assert.Equal(mode=="Exclusive"?0:1,Convert.ToInt32(await compatibility.ExecuteScalarAsync()));
            await probing.RollbackAsync();
            if(mode!="Exclusive")
                Assert.Equal(baseline.Totals,(await reporting.ExploreAsync(user,request)).Totals);
            await held.RollbackAsync();
        }
    }

    [Fact]
    public async Task Projection_only_explorer_reconciles_returns_services_charges_filtered_details_and_customer_universe()
    {
        var business=Guid.NewGuid();var invoice=Guid.NewGuid();var historical=Guid.NewGuid();var service=Guid.NewGuid();var returned=Guid.NewGuid();
        var customerA=Guid.NewGuid();var customerB=Guid.NewGuid();var customerC=Guid.NewGuid();
        var supplierA=Guid.NewGuid();var supplierB=Guid.NewGuid();var waiterA=Guid.NewGuid();var waiterB=Guid.NewGuid();
        var seller=Guid.NewGuid();var billedBy=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var seed=new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,PointOfSaleMode,TimeZone)
            VALUES(@Business,@Tenant,N'Conciliación de reportes',N'',N'',N'',N'',N'',N'RestaurantBar',N'UTC');
            INSERT reporting.SalesReportDocuments(DocumentId,TenantId,BusinessId,DocumentType,DocumentNumber,IssuedAt,
              BusinessLocalDate,TimeZoneId,WarehouseId,WarehouseName,SellerName,CustomerId,CustomerIdentification,CustomerName,SourceMode,
              GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,CreditAmount,CollectedAmount,RecognizedCostAmount,
              ProjectionVersion,SourcePayloadHash,ProjectedAt,ProductQuantity,MissingCostLineCount)
            SELECT Id,@Tenant,@Business,Type,Number,CONVERT(datetimeoffset,Date),Date,N'UTC',Warehouse,N'Principal',N'Sin vendedor',Customer,N'ID',Name,N'Online',
              Base+CASE WHEN Number=N'ACTUAL' THEN 10 ELSE 0 END,CASE WHEN Number=N'ACTUAL' THEN 10 ELSE 0 END,Base,Tax,Base+Tax,0,Base+Tax,Cost,5,HASHBYTES('SHA2_256',N'fixture'),SYSDATETIMEOFFSET(),
              CASE WHEN Type=N'ServiceInvoice' THEN 0 WHEN Number=N'ACTUAL' THEN 3 ELSE 1 END,CASE WHEN Type=N'ServiceInvoice' THEN 1 ELSE 0 END
            FROM (VALUES
              (@Invoice,N'SalesInvoice',N'ACTUAL',CONVERT(date,'20261101'),@Warehouse,@CustomerA,N'Cliente A',320.0,41.8,CONVERT(decimal(19,4),180)),
              (@Historical,N'SalesInvoice',N'HISTÓRICA',CONVERT(date,'20260901'),@Warehouse,@CustomerB,N'Cliente B',100.0,19.0,CONVERT(decimal(19,4),60)),
              (@Service,N'ServiceInvoice',N'SERVICIO',CONVERT(date,'20261102'),NULL,@CustomerC,N'Cliente C',50.0,9.5,NULL)
            ) d(Id,Type,Number,Date,Warehouse,Customer,Name,Base,Tax,Cost);
            INSERT reporting.SalesReportLineFacts(FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
              OriginalSaleDocumentId,OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,WarehouseId,CustomerId,ProductId,
              ProductCode,ProductName,SupplierId,SupplierName,WaiterPartyId,WaiterName,UnitCode,TaxCode,TaxRate,
              Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,RecognizedCostAmount,CostAvailable,ProjectionVersion,ProjectedAt)
            SELECT NEWID(),@Tenant,@Business,Id,Type,Line,Original,Line,Movement,CONVERT(datetimeoffset,Date),Date,@Warehouse,Customer,Product,
              Code,Code,Supplier,SupplierName,Waiter,WaiterName,N'EA',N'IVA',Rate,
              Quantity,Base+CASE WHEN Id=@Invoice AND Line=1 THEN 10 ELSE 0 END,CASE WHEN Id=@Invoice AND Line=1 THEN 10 ELSE 0 END,Base,Tax,Base+Tax,Cost,1,5,SYSDATETIMEOFFSET()
            FROM (VALUES
              (@Invoice,N'SalesInvoice',1,@Invoice,N'Sale',CONVERT(date,'20261101'),@CustomerA,@ProductA,N'Producto A',@SupplierA,N'Proveedor A',@WaiterA,N'Mesero A',19.0,2.0,200.0,38.0,120.0),
              (@Invoice,N'SalesInvoice',2,@Invoice,N'Sale',CONVERT(date,'20261101'),@CustomerA,@ProductB,N'Producto B',@SupplierB,N'Proveedor B',@WaiterB,N'Mesero B',0.0,1.0,100.0,0.0,60.0),
              (@Historical,N'SalesInvoice',1,@Historical,N'Sale',CONVERT(date,'20260901'),@CustomerB,@ProductA,N'Producto A',@SupplierA,N'Proveedor A',@WaiterA,N'Mesero A',19.0,1.0,100.0,19.0,60.0),
              (@Return,N'SalesReturn',1,@Historical,N'Return',CONVERT(date,'20261103'),@CustomerB,@ProductA,N'Producto A',@SupplierA,N'Proveedor A',@WaiterA,N'Mesero A',19.0,-1.0,-100.0,-19.0,-60.0)
            ) l(Id,Type,Line,Original,Movement,Date,Customer,Product,Code,Supplier,SupplierName,Waiter,WaiterName,Rate,Quantity,Base,Tax,Cost);
            INSERT reporting.ServiceSalesReportLineFacts(FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
              OriginalSaleDocumentId,MovementType,OccurredAt,BusinessLocalDate,CustomerId,BillableServiceId,ServiceCode,ServiceName,
              UnitCode,Quantity,GrossAmount,DiscountAmount,UntaxedAmount,TaxCode,TaxRate,TaxAmount,TotalAmount,ProjectionVersion,ProjectedAt)
            VALUES(NEWID(),@Tenant,@Business,@Service,N'ServiceInvoice',1,@Service,N'Sale','2026-11-02','20261102',@CustomerC,NEWID(),N'SV',N'Servicio',
              N'EA',1,50,0,50,N'IVA',19,9.5,59.5,5,SYSDATETIMEOFFSET());
            INSERT reporting.SalesReportTaxFacts(SourceDocumentId,SourceDocumentType,OriginalSaleDocumentId,TaxCode,TaxRate,TenantId,BusinessId,
              BusinessLocalDate,TaxableAmount,TaxAmount,TotalAmount,ProjectedAt,OccurredAt)
            SELECT Id,Type,Original,N'IVA',Rate,@Tenant,@Business,Date,Base,Tax,Base+Tax,SYSDATETIMEOFFSET(),CONVERT(datetimeoffset,Date)
            FROM (VALUES
              (@Invoice,N'SalesInvoice',@Invoice,19.0,CONVERT(date,'20261101'),220.0,41.8),
              (@Invoice,N'SalesInvoice',@Invoice,0.0,CONVERT(date,'20261101'),100.0,0.0),
              (@Historical,N'SalesInvoice',@Historical,19.0,CONVERT(date,'20260901'),100.0,19.0),
              (@Return,N'SalesReturn',@Historical,19.0,CONVERT(date,'20261103'),-100.0,-19.0),
              (@Service,N'ServiceInvoice',@Service,19.0,CONVERT(date,'20261102'),50.0,9.5)
            ) t(Id,Type,Original,Rate,Date,Base,Tax);
            INSERT reporting.SalesReportPaymentFacts(SourceDocumentId,SourceDocumentType,PaymentNumber,TenantId,BusinessId,
              BusinessLocalDate,MovementType,MethodCode,Amount,ProjectedAt)
            VALUES(@Invoice,N'SalesInvoice',1,@Tenant,@Business,'20261101',N'Payment',N'Cash',400,SYSDATETIMEOFFSET()),
              (@Return,N'SalesReturn',1,@Tenant,@Business,'20261103',N'Refund',N'Cash',-119,SYSDATETIMEOFFSET()),
              (@Service,N'ServiceInvoice',1,@Tenant,@Business,'20261102',N'Payment',N'Cash',59.5,SYSDATETIMEOFFSET());
            UPDATE reporting.SalesReportDocuments SET TotalAmount=400,CollectedAmount=400,RoundingAdjustmentAmount=38.2 WHERE DocumentId=@Invoice;
            UPDATE reporting.SalesReportDocuments SET SellerId=@Seller,SellerName=N'Vendedor original',BilledByUserId=@BilledBy,BilledByName=N'Cajero original' WHERE BusinessId=@Business;
            UPDATE reporting.SalesReportLineFacts SET SellerId=@Seller WHERE BusinessId=@Business;
            UPDATE reporting.ServiceSalesReportLineFacts SET SellerId=@Seller WHERE BusinessId=@Business;
            INSERT reporting.PurchaseReportLineFacts(PurchaseFactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
              OccurredAt,BusinessLocalDate,SupplierId,SupplierName,WarehouseId,WarehouseName,ProductId,ProductName,Quantity,UnitCost,
              DiscountAmount,NetAmount,TaxAmount,TotalAmount,CurrencyCode,FunctionalNetAmount,FunctionalTaxAmount,FunctionalTotalAmount,
              ExchangeRate,ProjectionVersion,ProjectedAt)
            SELECT NEWID(),@Tenant,@Business,NEWID(),Type,1,CONVERT(datetimeoffset,Date),Date,@SupplierA,N'Proveedor A',@Warehouse,N'Principal',
              @ProductA,N'Producto A',Qty,UnitCost,0,Base,Tax,Total,Currency,FunctionalBase,FunctionalTax,FunctionalTotal,Rate,5,SYSDATETIMEOFFSET()
            FROM(VALUES
              (N'GoodsReceipt',CONVERT(date,'20261101'),10,10.0,100.0,19.0,119.0,N'USD',400000.0,76000.0,476000.0,4000.0),
              (N'PurchaseReturn',CONVERT(date,'20261102'),-1,10000.0,-10000.0,-1900.0,-11900.0,N'COP',-10000.0,-1900.0,-11900.0,1.0),
              (N'GoodsReceipt',CONVERT(date,'20261010'),1,50000.0,50000.0,0.0,50000.0,N'COP',50000.0,0.0,50000.0,1.0)
            ) p(Type,Date,Qty,UnitCost,Base,Tax,Total,Currency,FunctionalBase,FunctionalTax,FunctionalTotal,Rate);
            """,connection);
        foreach(var pair in new (string,Guid)[]{("@Business",business),("@Tenant",fixture.TenantId),("@Warehouse",fixture.WarehouseId),
                    ("@Invoice",invoice),("@Historical",historical),("@Service",service),("@Return",returned),
                    ("@CustomerA",customerA),("@CustomerB",customerB),("@CustomerC",customerC),("@SupplierA",supplierA),("@SupplierB",supplierB),
                    ("@WaiterA",waiterA),("@WaiterB",waiterB),("@ProductA",Guid.NewGuid()),("@ProductB",Guid.NewGuid()),("@Seller",seller),("@BilledBy",billedBy)})
            seed.Parameters.AddWithValue(pair.Item1,pair.Item2);
        await seed.ExecuteNonQueryAsync();
        await using(var daily=new SqlCommand("""
            INSERT reporting.SalesReportDailyTotals(BusinessId,BusinessLocalDate,CurrencyCode,DocumentCount,UnitsSold,UnitsReturned,
              GrossSales,Discounts,Returns,NetUntaxedSales,NetTax,NetTotalSales,NetRecognizedCost,GrossProfit,
              UnknownCostDocumentCount,CreditSales,Collected,Refunded,ProjectionVersion,UpdatedAt)
            SELECT @Business,Date,N'COP',Documents,Sold,Returned,Gross,Discount,Returns,Net,Tax,Official,Cost,Profit,UnknownCost,0,Collected,Refunded,5,SYSDATETIMEOFFSET()
            FROM(VALUES
              (CONVERT(date,'20261101'),1,3,0,330,10,0,320,41.8,400,180,140,0,400,0),
              (CONVERT(date,'20261102'),1,0,0,50,0,0,50,9.5,59.5,0,0,1,59.5,0),
              (CONVERT(date,'20261103'),0,0,1,0,0,100,-100,-19,-119,-60,-40,0,0,119)
            ) v(Date,Documents,Sold,Returned,Gross,Discount,Returns,Net,Tax,Official,Cost,Profit,UnknownCost,Collected,Refunded);
            """,connection))
        {daily.Parameters.AddWithValue("@Business",business);await daily.ExecuteNonQueryAsync();}
        var areaA=Guid.NewGuid();var areaB=Guid.NewGuid();var lineA=Guid.NewGuid();var lineB=Guid.NewGuid();
        var groupA=Guid.NewGuid();var groupB=Guid.NewGuid();var subgroupA=Guid.NewGuid();var subgroupB=Guid.NewGuid();
        var hierarchyA=System.Text.Json.JsonSerializer.Serialize(new {AreaId=areaA,AreaName="Área A",LineId=lineA,LineName="Línea A",GroupId=groupA,GroupName="Grupo A",SubgroupId=subgroupA,SubgroupName="Subgrupo A"});
        var hierarchyB=System.Text.Json.JsonSerializer.Serialize(new {AreaId=areaB,AreaName="Área B",LineId=lineB,LineName="Línea B",GroupId=groupB,GroupName="Grupo B",SubgroupId=subgroupB,SubgroupName="Subgrupo B"});
        var productA=(Guid)seed.Parameters["@ProductA"].Value;
        var productB=(Guid)seed.Parameters["@ProductB"].Value;
        await using(var classify=new SqlCommand("UPDATE reporting.SalesReportLineFacts SET CategoryHierarchyJson=CASE WHEN SupplierId=@A THEN @PathA ELSE @PathB END WHERE BusinessId=@Business; UPDATE reporting.PurchaseReportLineFacts SET CategoryHierarchyJson=@PathA WHERE BusinessId=@Business;",connection))
        {classify.Parameters.AddWithValue("@Business",business);classify.Parameters.AddWithValue("@A",supplierA);classify.Parameters.AddWithValue("@PathA",hierarchyA);classify.Parameters.AddWithValue("@PathB",hierarchyB);await classify.ExecuteNonQueryAsync();}
        using var scope=fixture.CreateScope();var reporting=scope.ServiceProvider.GetRequiredService<SalesReportingService>();
        var user=new SalesReportingUserIdentity(fixture.UserId,fixture.TenantId,business,
            new HashSet<string>{SalesReportingPermissionCodes.Read,SalesReportingPermissionCodes.ReadAll});
        var filter=new SalesReportFilter(new(2026,11,1),new(2026,11,30));
        SalesReportExplorerPage all;
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            all=await reporting.ExploreAsync(user,new(filter,PageSize:1));Assert.Equal(2,counter.Count);
        }
        Assert.Equal(3,all.TotalCount);Assert.Single(all.Items);Assert.Equal(270m,all.Totals.NetUntaxed);
        Assert.Equal(380m,all.Totals.GrossSales);Assert.Equal(10m,all.Totals.Discounts);
        Assert.Equal(370m,all.Totals.SalesBeforeReturns);Assert.Equal(100m,all.Totals.Returns);
        Assert.Equal(421.3m,all.Totals.SalesWithTax);Assert.Equal(119m,all.Totals.ReturnsWithTax);
        Assert.Equal(51.3m,all.Totals.SalesTax);Assert.Equal(19m,all.Totals.ReturnedTax);
        Assert.Equal(32.3m,all.Totals.Tax);Assert.Equal(302.3m,all.Totals.NetTotal);Assert.Equal(2m,all.Totals.Quantity);
        Assert.Equal(340.5m,all.OfficialTotal);Assert.Equal(38.2m,all.RoundingAdjustment);
        Assert.Null(all.Totals.Profit);Assert.Null(all.Totals.MarginPercent);
        var emptyPage=await reporting.ExploreAsync(user,new(filter,Page:4,PageSize:1));
        Assert.Empty(emptyPage.Items);Assert.Equal(all.TotalCount,emptyPage.TotalCount);Assert.Equal(all.Totals,emptyPage.Totals);
        var suppliers=await reporting.ExploreAsync(user,new(filter,GroupBy:"supplier"));
        Assert.Equal(all.Totals.NetUntaxed,suppliers.Items.Sum(row=>row.Measures.NetUntaxed));
        var scoped=await reporting.ExploreAsync(user,new(filter with {SupplierId=supplierB},DocumentId:invoice));
        Assert.Single(scoped.Items);Assert.Equal(100m,scoped.Totals.NetUntaxed);Assert.Equal(40m,scoped.Totals.Profit);
        Assert.Equal(40m,scoped.Totals.MarginPercent);Assert.Empty(scoped.Payments);Assert.Single(scoped.Taxes);
        Assert.Equal(0m,scoped.Taxes[0].Tax);Assert.Null(scoped.RoundingAdjustment);
        var waiter=await reporting.ExploreAsync(user,new(filter,WaiterPartyId:waiterB));Assert.Equal(scoped.Totals,waiter.Totals);
        var combined=new SalesReportExplorerRequest(filter with {CustomerId=customerA,SupplierId=supplierB,SellerId=seller},
            GroupBy:"seller",BilledByUserId:billedBy,WaiterPartyId:waiterB);
        var combinedPage=await reporting.ExploreAsync(user,combined);
        Assert.Equal(seller,Guid.Parse(Assert.Single(combinedPage.Items).Key));Assert.Equal(scoped.Totals,combinedPage.Totals);
        var combinedDocuments=await reporting.ExploreAsync(user,combined with {GroupBy="document",DrillDimension="seller",DrillKey=seller.ToString()});
        Assert.Equal(invoice,Assert.Single(combinedDocuments.Items).DocumentId);Assert.Equal(combinedPage.Totals,combinedDocuments.Totals);
        var combinedLines=await reporting.ExploreAsync(user,combined with {DocumentId=invoice});
        Assert.Single(combinedLines.Items);Assert.Equal(combinedPage.Totals,combinedLines.Totals);Assert.Empty(combinedLines.Payments);
        var excludedCombination=await reporting.ExploreAsync(user,combined with {Filter=combined.Filter with {CustomerId=customerB}});
        Assert.Empty(excludedCombination.Items);Assert.Equal(0m,excludedCombination.Totals.NetUntaxed);
        var excludedUser=await reporting.ExploreAsync(user,combined with {BilledByUserId=Guid.NewGuid()});Assert.Empty(excludedUser.Items);
        var excludedDates=await reporting.ExploreAsync(user,combined with {Filter=combined.Filter with {From=new(2026,11,2)}});Assert.Empty(excludedDates.Items);
        var original=await reporting.ExploreAsync(user,new(filter,DocumentId:invoice));
        Assert.Equal(3,original.TotalCount);Assert.Equal(320m,original.Totals.NetUntaxed);Assert.Equal(140m,original.Totals.Profit);
        Assert.Equal(400m,Assert.Single(original.Payments).Amount);Assert.Equal(400m,original.OfficialTotal);Assert.Equal(38.2m,original.RoundingAdjustment);Assert.Contains(original.Items,row=>row.ItemKind=="Charge");
        var returns=await reporting.ExploreAsync(user,new(filter,DocumentId:historical));
        Assert.Equal(-100m,returns.Totals.NetUntaxed);Assert.Equal(-60m,returns.Totals.RecognizedCost);Assert.Single(returns.Items);
        var impact=await reporting.ExploreAsync(user,new(filter,Module:"impact",GroupBy:"customer"));
        Assert.Equal(3,impact.Impact!.KnownCustomers);Assert.Equal(2,impact.Impact.Buyers);Assert.Equal(1,impact.Impact.WithoutPurchase);
        var portfolio=await reporting.ExploreAsync(user,new(filter with {SupplierId=supplierA},Module:"impact",GroupBy:"customer"));
        Assert.Equal(3,portfolio.Impact!.KnownCustomers);Assert.Equal(1,portfolio.Impact.Buyers);Assert.Equal(2,portfolio.Impact.WithoutPurchase);
        var without=await reporting.ExploreAsync(user,new(filter with {SupplierId=supplierA},Module:"impact",CustomerActivity:"without-purchase"));
        Assert.Equal(2,without.TotalCount);Assert.All(without.Items,row=>Assert.False(row.BoughtInPeriod));
        Assert.Contains(without.Items,row=>Guid.TryParse(row.Key,out var id)&&id==customerB);
        Assert.Equal(-100m,without.Totals.NetUntaxed);
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var summary=await reporting.GetSummaryAsync(user,filter with {CustomerId=customerA},null,null);
            Assert.Equal(2,counter.Count);Assert.Equal(320m,summary.Current.NetUntaxedSales);
            Assert.Equal(400m,summary.Current.NetTotalSales);Assert.Equal(400m,summary.Current.Collected);
            Assert.Equal(140m,summary.Current.GrossProfit);
        }
        var supplierSummary=await reporting.GetSummaryAsync(user,filter with {SupplierId=supplierB},null,null);
        Assert.Equal(100m,supplierSummary.Current.NetUntaxedSales);Assert.Null(supplierSummary.Current.Collected);
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var buying=await reporting.ExploreAsync(user,new(filter,Module:"supplier",GroupBy:"supplier",Comparison:"previous-period"));
            Assert.Equal(2,counter.Count);Assert.Equal(2,buying.TotalCount);Assert.Equal(1,buying.Totals.Customers);
            Assert.Equal(200m,buying.Totals.NetUntaxed);Assert.Equal(80m,buying.Totals.Profit);
            Assert.Equal(3,buying.Supplier!.KnownCustomers);Assert.Equal(390000m,buying.Supplier.NetPurchases);
            Assert.Equal(400000m,buying.Supplier.PurchaseReceipts);Assert.Equal(10000m,buying.Supplier.PurchaseReturns);
            Assert.Equal(74100m,buying.Supplier.PurchaseTax);Assert.Equal(464100m,buying.Supplier.PurchasesWithTax);
            Assert.Equal(50000m,buying.Supplier.PreviousNetPurchases);
            var purchased=Assert.Single(buying.Items,row=>Guid.Parse(row.Key)==supplierA).Supplier!;
            Assert.Equal(390000m,purchased.NetPurchases);Assert.Equal(9m,purchased.PurchaseQuantity);
            Assert.Equal(50000m,purchased.PreviousNetPurchases);
        }
        var tableA=Guid.NewGuid();var tableB=Guid.NewGuid();
        await using(var tables=new SqlCommand("UPDATE reporting.SalesReportDocuments SET TableId=CASE WHEN CustomerId=@Customer THEN @A ELSE @B END,TableName=CASE WHEN CustomerId=@Customer THEN N'Mesa A' ELSE N'Mesa B' END WHERE BusinessId=@Business AND DocumentType<>N'ServiceInvoice';",connection))
        {tables.Parameters.AddWithValue("@Business",business);tables.Parameters.AddWithValue("@Customer",customerA);tables.Parameters.AddWithValue("@A",tableA);tables.Parameters.AddWithValue("@B",tableB);await tables.ExecuteNonQueryAsync();}
        var tablesForWaiter=await reporting.ExploreAsync(user,new(filter with {WaiterPartyIds=[waiterB],TableIds=[tableA,tableB]},GroupBy:"table"));
        Assert.Equal("Mesa A",Assert.Single(tablesForWaiter.Items).Label);Assert.Equal(100m,tablesForWaiter.Totals.NetUntaxed);Assert.Equal(60m,tablesForWaiter.Totals.RecognizedCost);
        var multiple=filter with {CustomerIds=[customerA,customerC],SupplierIds=[supplierB,Guid.NewGuid()],SellerIds=[seller],
            BilledByUserIds=[billedBy],WaiterPartyIds=[waiterB],AreaIds=[areaB],LineIds=[lineB],GroupIds=[groupB],SubgroupIds=[subgroupB]};
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var selected=await reporting.ExploreAsync(user,new(multiple,GroupBy:"seller"));
            Assert.Equal(2,counter.Count);Assert.Equal(100m,selected.Totals.NetUntaxed);Assert.Equal(60m,selected.Totals.RecognizedCost);
        }
        var summaryMultiple=await reporting.GetSummaryAsync(user,multiple,null,null);
        Assert.Equal(100m,summaryMultiple.Current.NetUntaxedSales);Assert.Null(summaryMultiple.Current.Collected);
        Assert.Equal(1,summaryMultiple.CustomerCount);
        var union=await reporting.ExploreAsync(user,new(filter with {CustomerIds=[customerA,customerC]}));
        Assert.Equal(370m,union.Totals.NetUntaxed);
        var supplierFiltered=await reporting.ExploreAsync(user,new(multiple with {SupplierIds=[supplierA,supplierB],WaiterPartyIds=[waiterA,waiterB],AreaIds=[],LineIds=[],GroupIds=[],SubgroupIds=[]},Module:"supplier",GroupBy:"supplier"));
        Assert.Equal(300m,supplierFiltered.Totals.NetUntaxed);Assert.Equal(390000m,supplierFiltered.Supplier!.NetPurchases);
        var categoryFiltered=await reporting.ExploreAsync(user,new(filter with {AreaIds=[areaB]},Module:"supplier",GroupBy:"supplier"));
        Assert.Equal(100m,categoryFiltered.Totals.NetUntaxed);Assert.Equal(0m,categoryFiltered.Supplier!.NetPurchases);
        var path=new SalesReportDrillStep[]{new("area",areaA.ToString()),new("category-line",lineA.ToString()),new("category-group",groupA.ToString()),new("subgroup",subgroupA.ToString())};
        var productsByPath=await reporting.ExploreAsync(user,new(filter,GroupBy:"product",DrillPath:path));
        Assert.Single(productsByPath.Items);Assert.Equal(100m,productsByPath.Totals.NetUntaxed);
        var wrongPath=await reporting.ExploreAsync(user,new(filter,GroupBy:"product",DrillPath:[new("area",areaA.ToString()),new("category-line",lineB.ToString())]));
        Assert.Empty(wrongPath.Items);Assert.Equal(0m,wrongPath.Totals.NetUntaxed);
        await Assert.ThrowsAsync<SalesReportingValidationException>(()=>reporting.ExploreAsync(user,new(filter with {CustomerIds=Enumerable.Range(0,101).Select(_=>Guid.NewGuid()).ToArray()})));

        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var tableOptions=await reporting.GetFilterOptionsAsync(user,new(filter,"table"));
            Assert.Equal(2,counter.Count);Assert.Equal(2,tableOptions.TotalCount);
        }
        await using(var classify=new SqlCommand("UPDATE reporting.SalesReportLineFacts SET CompositionKind=CASE WHEN ProductId=@A THEN N'Ingredients' ELSE N'Combo' END WHERE BusinessId=@Business;",connection))
        {classify.Parameters.AddWithValue("@A",productA);classify.Parameters.AddWithValue("@Business",business);await classify.ExecuteNonQueryAsync();}
        var macro=await reporting.GetSummaryAsync(user,filter,null,null);
        Assert.Equal(2,macro.Current.DocumentCount);Assert.Equal(3,all.Totals.Documents); // Issued in period vs original documents with movements.
        Assert.Equal(all.Totals.Quantity,macro.Current.UnitsSold-macro.Current.UnitsReturned);
        Assert.Equal(all.Totals.NetUntaxed,macro.Current.NetUntaxedSales);Assert.Equal(all.Totals.Tax,macro.Current.NetTax);
        Assert.Equal(all.Totals.NetTotal,macro.ExactNetTotal);Assert.Equal(all.OfficialTotal,macro.Current.NetTotalSales);
        Assert.Equal(all.RoundingAdjustment,macro.RoundingAdjustment);Assert.Equal(all.Totals.RecognizedCost,macro.Current.NetRecognizedCost);
        Assert.Equal(all.Totals.Profit,macro.Current.GrossProfit);Assert.Equal(all.Totals.MarginPercent,macro.Current.GrossMarginPercent);
        // One summary request owns every panel; charts retain the existing
        // breakdown semantics, including services, charges and historical returns.
        foreach(var chartFilter in new[]{filter,filter with {CustomerIds=[customerA]},
            filter with {SupplierIds=[supplierA,supplierB]},filter with {WaiterPartyIds=[waiterA]},
            filter with {From=new(2026,12,1),To=new(2026,12,2)}})
        {
            var expected=await reporting.GetSummaryAsync(user,chartFilter,new(2026,9,1),new(2026,9,30));
            SalesReportSummary bundled;
            using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
            {
                bundled=await reporting.GetSummaryAsync(user,chartFilter,new(2026,9,1),new(2026,9,30),includeBreakdowns:true);
                Assert.Equal(2,counter.Count);
            }
            Assert.Equal(expected.Current,bundled.Current);Assert.Equal(expected.Comparison,bundled.Comparison);
            Assert.Equal(expected.Trend.ToArray(),bundled.Trend.ToArray());Assert.Equal(expected.CustomerCount,bundled.CustomerCount);
            var charts=Assert.IsType<SalesReportSummaryBreakdowns>(bundled.Breakdowns);
            foreach(var (dimension,limit,rows) in new[]{("hour",25,charts.Hourly),("seller",8,charts.Sellers),
                ("product",8,charts.Products),("supplier",8,charts.Suppliers),("warehouse",8,charts.Warehouses)})
                Assert.Equal((await reporting.GetBreakdownAsync(user,chartFilter,dimension,limit)).ToArray(),rows.ToArray());
            if(bundled.HasLineScope)Assert.Empty(charts.Payments);
            else Assert.Equal((await reporting.GetBreakdownAsync(user,chartFilter,"payment-method",20)).ToArray(),charts.Payments.ToArray());
        }
        foreach(var dimension in new[]{"document","customer","seller","supplier","billed-by","warehouse","waiter","table","area","category-line","category-group","subgroup","product","day","month"})
        {
            var grouped=await reporting.ExploreAsync(user,new(filter,GroupBy:dimension,PageSize:200));
            Assert.Equal(all.Totals,grouped.Totals);Reconcile(grouped.Totals,grouped.Items);
        }
        var allDocuments=await reporting.ExploreAsync(user,new(filter,PageSize:200));
        Assert.Equal(all.OfficialTotal,allDocuments.Items.Sum(row=>row.OfficialTotal));Assert.Equal(all.RoundingAdjustment,allDocuments.Items.Sum(row=>row.RoundingAdjustment));
        foreach(var documentRow in allDocuments.Items)
        {
            var rows=new List<SalesReportExplorerRow>();SalesReportMeasures? detailTotals=null;
            for(var detailPage=1;;detailPage++)
            {
                var details=await reporting.ExploreAsync(user,new(filter,DocumentId:documentRow.DocumentId,Page:detailPage,PageSize:1));
                if(detailTotals is not null)Assert.Equal(detailTotals,details.Totals);detailTotals=details.Totals;
                rows.AddRange(details.Items);Assert.Equal(documentRow.Measures.Tax,details.Taxes.Sum(tax=>tax.Tax));
                if(detailPage>=details.TotalCount)break;
            }
            Reconcile(documentRow.Measures,rows,compareQuantity:false);
        }
        Assert.Equal(all.Totals,(await reporting.ExploreAsync(user,new(filter,Module:"impact"))).Totals);
        var physical=await reporting.ExploreAsync(user,new(filter,GroupBy:"product",Module:"products",PageSize:200));
        var purchasesAndSales=await reporting.ExploreAsync(user,new(filter,Module:"supplier",PageSize:200));
        Assert.Equal(physical.Totals,purchasesAndSales.Totals);Reconcile(physical.Totals,physical.Items);Reconcile(purchasesAndSales.Totals,purchasesAndSales.Items);
        Assert.Equal(390000m,purchasesAndSales.Supplier!.NetPurchases);
        Assert.Equal(purchasesAndSales.Supplier.NetPurchases,purchasesAndSales.Items.Sum(row=>row.Supplier!.NetPurchases));
        Assert.Equal(purchasesAndSales.Supplier.PurchaseReceipts-purchasesAndSales.Supplier.PurchaseReturns,purchasesAndSales.Supplier.NetPurchases);
        Assert.Equal(all.Totals.NetUntaxed,physical.Totals.NetUntaxed+70m); // 50 service + 20 charge, explicitly outside the physical-product views.
        Assert.Equal(all.Totals.Tax,physical.Totals.Tax+13.3m);Assert.Equal(all.Totals.NetTotal,physical.Totals.NetTotal+83.3m);
        var sameScope=filter with {SupplierIds=[supplierA,supplierB]};
        var filteredSale=await reporting.ExploreAsync(user,new(sameScope));
        Assert.Equal(filteredSale.Totals,(await reporting.ExploreAsync(user,new(sameScope,Module:"products"))).Totals);
        Assert.Equal(filteredSale.Totals,(await reporting.ExploreAsync(user,new(sameScope,Module:"supplier"))).Totals);
        Assert.Equal(filteredSale.Totals,(await reporting.ExploreAsync(user,new(sameScope,Module:"impact"))).Totals);
        var filteredMacro=await reporting.GetSummaryAsync(user,sameScope,null,null);
        Assert.Equal(filteredSale.Totals.NetUntaxed,filteredMacro.Current.NetUntaxedSales);
        Assert.Equal(filteredSale.Totals.NetTotal,filteredMacro.ExactNetTotal);Assert.Equal(filteredSale.Totals.Profit,filteredMacro.Current.GrossProfit);
        Assert.Equal(filteredSale.Totals.RecognizedCost,filteredMacro.Current.NetRecognizedCost);Assert.Equal(filteredSale.Totals.MarginPercent,filteredMacro.Current.GrossMarginPercent);
        var onlyPurchased=Guid.NewGuid();
        await using(var addPurchase=new SqlCommand("""
            INSERT reporting.PurchaseReportLineFacts(PurchaseFactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,OccurredAt,BusinessLocalDate,SupplierId,SupplierName,WarehouseId,WarehouseName,ProductId,ProductName,Quantity,UnitCost,DiscountAmount,NetAmount,TaxAmount,TotalAmount,CurrencyCode,FunctionalNetAmount,FunctionalTaxAmount,FunctionalTotalAmount,ExchangeRate,ProjectionVersion,ProjectedAt,CategoryHierarchyJson)
            SELECT TOP(1) NEWID(),TenantId,BusinessId,NEWID(),N'GoodsReceipt',1,OccurredAt,BusinessLocalDate,SupplierId,SupplierName,WarehouseId,WarehouseName,@Product,N'Solo comprado',Quantity,UnitCost,DiscountAmount,NetAmount,TaxAmount,TotalAmount,CurrencyCode,FunctionalNetAmount,FunctionalTaxAmount,FunctionalTotalAmount,ExchangeRate,ProjectionVersion,ProjectedAt,CategoryHierarchyJson
            FROM reporting.PurchaseReportLineFacts WHERE BusinessId=@Business AND SourceDocumentType=N'GoodsReceipt' AND BusinessLocalDate BETWEEN '20261101' AND '20261130' ORDER BY PurchaseFactId;
            """,connection))
        {addPurchase.Parameters.AddWithValue("@Product",onlyPurchased);addPurchase.Parameters.AddWithValue("@Business",business);await addPurchase.ExecuteNonQueryAsync();}
        Assert.Empty((await reporting.GetFilterOptionsAsync(user,new(filter,"product",Search:"Solo comprado"))).Items);
        var purchaseOptions=await reporting.GetFilterOptionsAsync(user,new(filter,"product",Search:"Solo comprado",IncludePurchases:true));
        Assert.Equal(onlyPurchased,Guid.Parse(Assert.Single(purchaseOptions.Items).Code));
        var productRequest=new SalesReportExplorerRequest(filter,GroupBy:"product",Module:"products",PageSize:1);
        SalesReportExplorerPage productFirst;
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {productFirst=await reporting.ExploreAsync(user,productRequest);Assert.Equal(2,counter.Count);}
        var productSecond=await reporting.ExploreAsync(user,productRequest with {Page=2});
        Assert.Equal(productFirst.Products,productSecond.Products);Assert.Equal(200m,productFirst.Products!.NetUntaxed);
        Assert.Equal(80m,productFirst.Products.Profit);Assert.Equal(2,productFirst.Products.Products);
        Assert.Equal(50m,Assert.Single(productFirst.Items).SalesParticipationPercent);
        Assert.Equal(50m,Assert.Single(productSecond.Items).ProfitParticipationPercent);
        var returnedProducts=productRequest with {Products=new(ReturnActivity:"with-returns")};
        var returnedPage=await reporting.ExploreAsync(user,returnedProducts);
        Assert.Equal(productA,Guid.Parse(Assert.Single(returnedPage.Items).Key));Assert.Equal(100m,returnedPage.Totals.NetUntaxed);
        var productDocs=await reporting.ExploreAsync(user,returnedProducts with {GroupBy="document",DrillPath=[new("product",productA.ToString())]});
        var productLines=await reporting.ExploreAsync(user,returnedProducts with {DocumentId=invoice,DrillPath=[new("product",productA.ToString())]});
        Assert.Equal(returnedPage.Products,productDocs.Products);Assert.Equal(returnedPage.Products,productLines.Products);
        Assert.Equal(200m,productLines.Totals.NetUntaxed); // The invoice alone has no return; eligibility remains the complete period.
        Assert.Single((await reporting.ExploreAsync(user,productRequest with {Products=new(ReturnActivity:"without-returns")})).Items);
        Assert.Equal(100m,(await reporting.ExploreAsync(user,productRequest with {Products=new(CompositionKind:"Ingredients")})).Totals.NetUntaxed);
        Assert.Equal(100m,(await reporting.ExploreAsync(user,productRequest with {Products=new(CompositionKind:"Combo")})).Totals.NetUntaxed);
        Assert.Empty((await reporting.ExploreAsync(user,productRequest with {Products=new(CompositionKind:"None")})).Items);
        Assert.Empty((await reporting.ExploreAsync(user,productRequest with {Products=new(MarginFrom:41m)})).Items);
        var returnOnly=productRequest with {Filter=filter with {From=new(2026,11,3)},Products=new(MarginFrom:39m,MarginTo:41m)};
        Assert.Equal(-100m,(await reporting.ExploreAsync(user,returnOnly)).Totals.NetUntaxed);
        Assert.Empty((await reporting.ExploreAsync(user,returnOnly with {Products=new(MarginFrom:41m)})).Items);
        Assert.Empty((await reporting.ExploreAsync(user,returnOnly with {Products=new(MarginTo:39m)})).Items);
        await using(var boundary=new SqlCommand("UPDATE reporting.SalesReportLineFacts SET RecognizedCostAmount=97.6550 WHERE BusinessId=@Business AND ProductId=@B;",connection))
        {boundary.Parameters.AddWithValue("@B",productB);boundary.Parameters.AddWithValue("@Business",business);await boundary.ExecuteNonQueryAsync();}
        Assert.Empty((await reporting.ExploreAsync(user,productRequest with {Products=new(MarginTo:2.34m)})).Items);
        Assert.Single((await reporting.ExploreAsync(user,productRequest with {Products=new(MarginFrom:2.34m,MarginTo:2.35m)})).Items);
        await Assert.ThrowsAsync<SalesReportingValidationException>(()=>reporting.ExploreAsync(user,productRequest with {Products=new(MarginTo:1.001m)}));
        await using(var loss=new SqlCommand("UPDATE reporting.SalesReportLineFacts SET RecognizedCostAmount=180 WHERE BusinessId=@Business AND ProductId=@B;",connection))
        {loss.Parameters.AddWithValue("@B",productB);loss.Parameters.AddWithValue("@Business",business);await loss.ExecuteNonQueryAsync();}
        var losing=await reporting.ExploreAsync(user,productRequest with {Products=new(MarginTo:0m)});
        Assert.Equal(-80m,losing.Totals.Profit);Assert.Null(Assert.Single(losing.Items).ProfitParticipationPercent);
        await using(var missing=new SqlCommand("UPDATE reporting.SalesReportLineFacts SET CostAvailable=0,CompositionKind=NULL WHERE BusinessId=@Business AND ProductId=@A;",connection))
        {missing.Parameters.AddWithValue("@A",productA);missing.Parameters.AddWithValue("@Business",business);await missing.ExecuteNonQueryAsync();}
        var unknown=await reporting.ExploreAsync(user,productRequest with {Products=new(CompositionKind:"Unknown")});
        Assert.Null(unknown.Totals.Profit);Assert.Null(Assert.Single(unknown.Items).ProfitParticipationPercent);
        Assert.Empty((await reporting.ExploreAsync(user,productRequest with {Products=new(MarginFrom:0m)})).Items);
        await Assert.ThrowsAsync<SalesReportingValidationException>(()=>reporting.ExploreAsync(user,productRequest with {Products=new(MarginFrom:50m,MarginTo:20m)}));
        await Assert.ThrowsAsync<SalesReportingValidationException>(()=>reporting.ExploreAsync(user,productRequest with {Module="sales",Products=new()}));
        await using(var mode=new SqlCommand("UPDATE dbo.Businesses SET PointOfSaleMode=N'DirectSale' WHERE BusinessId=@Business;",connection))
        {mode.Parameters.AddWithValue("@Business",business);await mode.ExecuteNonQueryAsync();}
        await Assert.ThrowsAsync<SalesReportingValidationException>(()=>reporting.ExploreAsync(user,new(filter,GroupBy:"table")));
        await Assert.ThrowsAsync<SalesReportingValidationException>(()=>reporting.ExploreAsync(user,new(filter with {TableIds=[tableA]})));
        Assert.DoesNotContain((await reporting.GetContextAsync(user)).Groupings,option=>option.Code is "table" or "waiter");

        static void Reconcile(SalesReportMeasures totals,IReadOnlyList<SalesReportExplorerRow> rows,bool compareQuantity=true)
        {
            Assert.Equal(totals.GrossSales,rows.Sum(row=>row.Measures.GrossSales));Assert.Equal(totals.Discounts,rows.Sum(row=>row.Measures.Discounts));
            Assert.Equal(totals.SalesBeforeReturns,rows.Sum(row=>row.Measures.SalesBeforeReturns));Assert.Equal(totals.Returns,rows.Sum(row=>row.Measures.Returns));
            Assert.Equal(totals.NetUntaxed,rows.Sum(row=>row.Measures.NetUntaxed));Assert.Equal(totals.Tax,rows.Sum(row=>row.Measures.Tax));
            Assert.Equal(totals.NetTotal,rows.Sum(row=>row.Measures.NetTotal));
            Assert.Equal(totals.ReturnsWithTax,rows.Sum(row=>row.Measures.ReturnsWithTax));Assert.Equal(totals.ReturnedTax,rows.Sum(row=>row.Measures.ReturnedTax));
            Assert.Equal(totals.SalesWithTax,rows.Sum(row=>row.Measures.SalesWithTax));Assert.Equal(totals.SalesTax,rows.Sum(row=>row.Measures.SalesTax));
            Assert.Equal(totals.RecognizedCost,rows.Any(row=>row.Measures.RecognizedCost is null)?null:rows.Sum(row=>row.Measures.RecognizedCost));
            Assert.Equal(totals.Profit,rows.Any(row=>row.Measures.Profit is null)?null:rows.Sum(row=>row.Measures.Profit));
            if(compareQuantity)Assert.Equal(totals.Quantity,rows.Sum(row=>row.Measures.Quantity));
            foreach(var measures in rows.Select(row=>row.Measures).Append(totals))
            {
                Assert.Equal(measures.GrossSales-measures.Discounts,measures.SalesBeforeReturns);
                Assert.Equal(measures.SalesBeforeReturns-measures.Returns,measures.NetUntaxed);
                Assert.Equal(measures.NetUntaxed+measures.Tax,measures.NetTotal);
                Assert.Equal(measures.RecognizedCost is {} cost?measures.NetUntaxed-cost:null,measures.Profit);
                Assert.Equal(measures.NetUntaxed!=0&&measures.Profit is {} profit?decimal.Round(profit/measures.NetUntaxed*100m,2):null,measures.MarginPercent);
            }
        }

    }
}
