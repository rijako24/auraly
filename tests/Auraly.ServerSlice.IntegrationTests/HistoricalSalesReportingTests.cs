using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auraly.Contracts.Sales;
using Auraly.Application.Sales;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HistoricalSalesReportingCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly historical sales reporting";
}

[Collection(HistoricalSalesReportingCollection.Name)]
[Trait("EngineCertification", "Reporting")]
public sealed class HistoricalSalesReportingTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Cost_cutover_retires_only_verified_fiscal_only_sources_without_commercial_effects(bool fiscalOnly, bool commercialEffects)
    {
        var sale = fixture.CreateValidRequest(9_620 + (fiscalOnly ? 1 : 0) + (commercialEffects ? 2 : 0));
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        var protectedEffects = await ReportingMigrationFingerprintAsync(connection);
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            var legacy = commercialEffects ? sale.DocumentId : Guid.NewGuid();
            await using var seed = new SqlCommand("""
                DECLARE @Json nvarchar(max),@Hash binary(32),@Job uniqueidentifier=NEWID();
                SELECT @Json=JSON_MODIFY(JSON_MODIFY(PayloadJson,'$.documentId',CONVERT(nvarchar(36),@Legacy)),
                    '$.fiscalHabilitationOnly',CONVERT(bit,@FiscalOnly))
                FROM dbo.DocumentProcessingPayloads WHERE DocumentId=@Original;
                SET @Hash=HASHBYTES('SHA2_256',CONVERT(varchar(max),@Json COLLATE Latin1_General_100_BIN2_UTF8));
                IF @Effects=1
                BEGIN
                  UPDATE dbo.DocumentProcessingPayloads SET PayloadJson=@Json,PayloadHash=@Hash WHERE DocumentId=@Original;
                  UPDATE dbo.SalesDocuments SET PayloadHash=@Hash WHERE DocumentId=@Original;
                END
                ELSE
                BEGIN
                  INSERT dbo.DocumentProcessingJobs(JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,
                    Status,AvailableAt,CreatedAt,StartedAt,CompletedAt)
                  SELECT @Job,BusinessId,(SELECT MAX(ProcessingSequence)+1 FROM dbo.DocumentProcessingJobs WHERE BusinessId=original.BusinessId),
                    @Legacy,DocumentType,N'Completed',CreatedAt,CreatedAt,StartedAt,CompletedAt
                  FROM dbo.DocumentProcessingJobs original WHERE DocumentId=@Original;
                  INSERT dbo.DocumentProcessingPayloads(DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
                  SELECT @Legacy,DocumentType,BusinessId,1,@Json,@Hash,AcceptedAt FROM dbo.DocumentProcessingPayloads WHERE DocumentId=@Original;
                  INSERT dbo.SalesDocuments(DocumentId,BusinessId,WarehouseId,DeviceId,SourceMode,DocumentSeriesId,
                    DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,DocumentType,IdempotencyKey,PayloadHash,
                    IssuedAt,CustomerIdentification,UntaxedAmount,TaxAmount,PayableAmount,ProcessingStatus,ReceivedAt,
                    FiscalSeriesId,FiscalAuthorizationId,FiscalNumber,FiscalPrefix,FiscalConsecutive,CufeReceived,FiscalStatus)
                  SELECT @Legacy,BusinessId,WarehouseId,DeviceId,SourceMode,DocumentSeriesId,
                    CONCAT(N'HAB-',CONVERT(nvarchar(36),@Legacy)),N'HAB',N'QA',1,DocumentType,CONVERT(nvarchar(36),@Legacy),@Hash,
                    IssuedAt,CustomerIdentification,UntaxedAmount,TaxAmount,PayableAmount,N'Completed',ReceivedAt,
                    FiscalSeriesId,FiscalAuthorizationId,CONCAT(N'HAB-',CONVERT(nvarchar(36),@Legacy)),N'HAB',1,CufeReceived,FiscalStatus
                  FROM dbo.SalesDocuments WHERE DocumentId=@Original;
                  INSERT reporting.SalesReportDocuments(DocumentId,TenantId,BusinessId,DocumentType,DocumentNumber,IssuedAt,
                    BusinessLocalDate,TimeZoneId,SellerName,CustomerIdentification,CustomerName,SourceMode,GrossAmount,
                    DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,CreditAmount,CollectedAmount,RecognizedCostAmount,
                    ProjectionVersion,SourcePayloadHash,ProjectedAt)
                  SELECT @Legacy,TenantId,BusinessId,DocumentType,CONCAT(N'HAB-',CONVERT(nvarchar(36),@Legacy)),IssuedAt,
                    BusinessLocalDate,TimeZoneId,SellerName,CustomerIdentification,CustomerName,SourceMode,GrossAmount,
                    DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,CreditAmount,CollectedAmount,RecognizedCostAmount,
                    ProjectionVersion,@Hash,ProjectedAt FROM reporting.SalesReportDocuments WHERE DocumentId=@Original;
                  SELECT * INTO #FiscalFacts FROM reporting.SalesReportLineFacts WHERE SourceDocumentId=@Original;
                  UPDATE #FiscalFacts SET FactId=NEWID(),SourceDocumentId=@Legacy,OriginalSaleDocumentId=@Legacy;
                  INSERT reporting.SalesReportLineFacts SELECT * FROM #FiscalFacts;
                  INSERT reporting.SalesReportingJobs(SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,
                    SourcePayloadHash,SourceDocumentProcessingJobId,Status,AttemptCount,CreatedAt)
                  SELECT NEWID(),BusinessId,@Legacy,DocumentType,@Hash,@Job,N'Projected',1,AcceptedAt
                  FROM dbo.DocumentProcessingPayloads WHERE DocumentId=@Legacy;
                END;
                """, connection, transaction);
            seed.Parameters.AddWithValue("@Original",sale.DocumentId); seed.Parameters.AddWithValue("@Legacy",legacy);
            seed.Parameters.AddWithValue("@FiscalOnly",fiscalOnly); seed.Parameters.AddWithValue("@Effects",commercialEffects);
            await seed.ExecuteNonQueryAsync();
            if (!fiscalOnly || commercialEffects)
            {
                seed.CommandText="DELETE reporting.SalesReportingJobs WHERE SourceDocumentId=@Legacy;";
                await seed.ExecuteNonQueryAsync();
                await fixture.ApplyMigrationAsync("BackfillEngineOwnedSourcesAndReportingJobs.sql",connection,transaction);
                await using var queued=new SqlCommand("SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Legacy;",connection,transaction);
                queued.Parameters.AddWithValue("@Legacy",legacy);
                Assert.Equal(1,Convert.ToInt32(await queued.ExecuteScalarAsync()));
                var error = await Assert.ThrowsAsync<SqlException>(() => fixture.ApplyMigrationAsync(
                    "20261125_FreezeInvoiceRecognizedCosts.sql", connection, transaction));
                Assert.Equal(51624,error.Number);
            }
            else
            {
                for (var replay=0;replay<2;replay++)
                {
                    await fixture.ApplyMigrationAsync("20261125_FreezeInvoiceRecognizedCosts.sql",connection,transaction);
                    await fixture.ApplyMigrationAsync("BackfillEngineOwnedSourcesAndReportingJobs.sql",connection,transaction);
                    await using var verify=new SqlCommand("SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Legacy;",connection,transaction);
                    verify.Parameters.AddWithValue("@Legacy",legacy);
                    Assert.Equal(0,Convert.ToInt32(await verify.ExecuteScalarAsync()));
                }
                // A true flag with another tenant is not proof of fiscal-only
                // ownership. Keep the source visible for canonical validation.
                seed.CommandText="""
                    UPDATE dbo.DocumentProcessingPayloads SET PayloadJson=JSON_MODIFY(PayloadJson,'$.tenantId',CONVERT(nvarchar(36),NEWID()))
                      WHERE DocumentId=@Legacy;
                    UPDATE dbo.DocumentProcessingPayloads SET PayloadHash=HASHBYTES('SHA2_256',CONVERT(varchar(max),PayloadJson COLLATE Latin1_General_100_BIN2_UTF8))
                      WHERE DocumentId=@Legacy;
                    UPDATE sale SET PayloadHash=payload.PayloadHash FROM dbo.SalesDocuments sale
                      JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=sale.DocumentId WHERE sale.DocumentId=@Legacy;
                    """;
                await seed.ExecuteNonQueryAsync();
                await fixture.ApplyMigrationAsync("BackfillEngineOwnedSourcesAndReportingJobs.sql",connection,transaction);
                await using var unproven=new SqlCommand("SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Legacy;",connection,transaction);
                unproven.Parameters.AddWithValue("@Legacy",legacy);
                Assert.Equal(1,Convert.ToInt32(await unproven.ExecuteScalarAsync()));
            }
            await transaction.RollbackAsync();
        }
        Assert.Equal(protectedEffects,await ReportingMigrationFingerprintAsync(connection));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task Invoice_cost_cutover_copies_historical_evidence_once_and_rejects_missing_evidence(
        bool generic, bool missingCost, bool missingLines)
    {
        var sale = fixture.CreateValidRequest(9_610 + (generic ? 1 : 0) + (missingCost ? 2 : 0) + (missingLines ? 4 : 0));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        if (generic)
        {
            await using var configure = new SqlCommand("UPDATE dbo.Products SET IsGenericProduct=1,ManageStock=0 WHERE ProductId=@Product;", connection);
            configure.Parameters.AddWithValue("@Product", fixture.ProductId);
            await configure.ExecuteNonQueryAsync();
            sale = sale with { Lines = sale.Lines.Select(line => line with { IsGenericProductSnapshot = true, DocumentUnitCost = 6000m }).ToArray() };
        }
        try
        {
            using var upload = fixture.CreateUploadMessage(sale);
            using var response = await fixture.CreateClient().SendAsync(upload);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            if (generic)
            {
                await using var restore = new SqlCommand("UPDATE dbo.Products SET IsGenericProduct=0,ManageStock=1 WHERE ProductId=@Product;", connection);
                restore.Parameters.AddWithValue("@Product", fixture.ProductId); await restore.ExecuteNonQueryAsync();
            }
        }
        var originalEffects = await ReportingMigrationFingerprintAsync(connection);
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await using var simulate = new SqlCommand("""
                ALTER TABLE dbo.SalesDocumentLines ALTER COLUMN RecognizedCostAmount decimal(19,4) NULL;
                SELECT RecognizedCostAmount FROM dbo.SalesDocumentLines WHERE DocumentId=@Document;
                UPDATE dbo.SalesDocumentLines SET RecognizedCostAmount=NULL,
                  UnitCostSnapshot=CASE WHEN @MissingCost=1 THEN NULL ELSE UnitCostSnapshot END WHERE DocumentId=@Document;
                IF @MissingLines=1 DELETE dbo.SalesDocumentLines WHERE DocumentId=@Document;
                """, connection, transaction);
            simulate.Parameters.AddWithValue("@Document", sale.DocumentId);
            simulate.Parameters.AddWithValue("@MissingCost", missingCost);
            simulate.Parameters.AddWithValue("@MissingLines", missingLines);
            var expected = Assert.IsType<decimal>(await simulate.ExecuteScalarAsync());
            if (missingCost || missingLines)
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => fixture.ApplyMigrationAsync(
                    "20261125_FreezeInvoiceRecognizedCosts.sql", connection, transaction));
                Assert.Equal(51624, error.Number);
            }
            else
            {
                for (var replay = 0; replay < 2; replay++)
                {
                    await fixture.ApplyMigrationAsync("20261125_FreezeInvoiceRecognizedCosts.sql", connection, transaction);
                    await using var verify = new SqlCommand("SELECT RecognizedCostAmount FROM dbo.SalesDocumentLines WHERE DocumentId=@Document;", connection, transaction);
                    verify.Parameters.AddWithValue("@Document", sale.DocumentId);
                    Assert.Equal(expected, Assert.IsType<decimal>(await verify.ExecuteScalarAsync()));
                }
            }
            await transaction.RollbackAsync();
        }
        Assert.Equal(originalEffects, await ReportingMigrationFingerprintAsync(connection));
    }

    [Fact]
    public async Task Non_stock_sale_freezes_its_declared_cost_before_projection_and_catalog_changes()
    {
        var sale = fixture.CreateValidRequest(9_586);
        sale = sale with { Lines = sale.Lines.Select(line => line with { DocumentUnitCost = 3694.476454m }).ToArray() };
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var configure = new SqlCommand("UPDATE dbo.Products SET ManageStock=0 WHERE ProductId=@Product;", connection);
        configure.Parameters.AddWithValue("@Product", fixture.ProductId); await configure.ExecuteNonQueryAsync();
        try
        {
            using var upload = fixture.CreateUploadMessage(sale);
            using var response = await fixture.CreateClient().SendAsync(upload);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            configure.CommandText = "UPDATE dbo.Products SET ManageStock=1 WHERE ProductId=@Product;";
            await configure.ExecuteNonQueryAsync();
        }
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>().RebuildAsync(fixture.TenantId, fixture.BusinessId, default);
        await using var query = new SqlCommand("""
            SELECT line.RecognizedCostAmount,fact.RecognizedCostAmount,fact.CostAvailable,
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Document)
            FROM dbo.SalesDocumentLines line JOIN reporting.SalesReportLineFacts fact
              ON fact.SourceDocumentId=line.DocumentId AND fact.SourceLineNumber=line.LineNumber
            WHERE line.DocumentId=@Document;
            """, connection);
        query.Parameters.AddWithValue("@Document", sale.DocumentId);
        await using var result = await query.ExecuteReaderAsync(); Assert.True(await result.ReadAsync());
        var expected = decimal.Round(sale.Lines[0].DocumentUnitCost * sale.Lines[0].Quantity, 4, MidpointRounding.AwayFromZero);
        Assert.Equal(expected,result.GetDecimal(0)); Assert.Equal(expected,result.GetDecimal(1));
        Assert.True(result.GetBoolean(2)); Assert.Equal(0,result.GetInt32(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rebuild_preserves_imported_return_facts_and_reports_them_once_without_inventing_sources(bool historicalSale)
    {
        var sale = fixture.CreateValidRequest(historicalSale ? 9_566 : 9_565);
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var accepted = await fixture.CreateClient().SendAsync(upload))
            Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var returned = Guid.NewGuid();
        var reportingSale = historicalSale ? Guid.NewGuid() : sale.DocumentId;
        var reportingBusiness = historicalSale ? Guid.NewGuid() : fixture.BusinessId;
        await using var seed = connection.CreateCommand();
        seed.Parameters.AddWithValue("@Sale", sale.DocumentId);
        seed.Parameters.AddWithValue("@Return", returned);
        seed.Parameters.AddWithValue("@ReportingBusiness", reportingBusiness);
        seed.Parameters.AddWithValue("@HistoricalSale", reportingSale);
        seed.CommandText = """
            INSERT reporting.SalesReportLineFacts(FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
              SourceLineNumber,OriginalSaleDocumentId,OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,
              WarehouseId,CustomerId,ProductId,ProductCode,ProductName,Quantity,GrossAmount,DiscountAmount,
              UntaxedAmount,TaxAmount,TotalAmount,RecognizedCostAmount,ReturnReasonCode,ReturnDisposition,
              ProjectionVersion,ProjectedAt,CostAvailable)
            SELECT NEWID(),TenantId,BusinessId,@Return,N'SalesReturn',SourceLineNumber,@Sale,SourceLineNumber,
              N'Return',DATEADD(day,1,OccurredAt),DATEADD(day,1,BusinessLocalDate),WarehouseId,CustomerId,
              ProductId,ProductCode,ProductName,-Quantity,-GrossAmount,-DiscountAmount,-UntaxedAmount,-TaxAmount,
              -TotalAmount,-RecognizedCostAmount,N'HISTORICAL',N'NotReturned',2,ProjectedAt,0
            FROM reporting.SalesReportLineFacts WHERE SourceDocumentId=@Sale AND MovementType=N'Sale';
            INSERT reporting.SalesReportTaxFacts(SourceDocumentId,SourceDocumentType,TaxCode,TaxRate,TenantId,
              BusinessId,BusinessLocalDate,TaxableAmount,TaxAmount,TotalAmount,ProjectedAt,OccurredAt)
            SELECT @Return,N'SalesReturn',v.Code,0,f.TenantId,f.BusinessId,DATEADD(day,1,f.BusinessLocalDate),
              -f.TaxableAmount/2,-f.TaxAmount/2,-f.TotalAmount/2,f.ProjectedAt,DATEADD(day,1,f.OccurredAt)
            FROM reporting.SalesReportTaxFacts f CROSS JOIN (VALUES(N'IMPORTED-A'),(N'IMPORTED-B')) v(Code)
            WHERE f.SourceDocumentId=@Sale;
            UPDATE reporting.SalesReportDocuments SET ReturnedUntaxedAmount=UntaxedAmount,
              ReturnedTaxAmount=TaxAmount,ReturnedTotalAmount=TotalAmount WHERE DocumentId=@Sale;
            """;
        await seed.ExecuteNonQueryAsync();
        if (historicalSale)
        {
            // Give the retained reporting-only graph its own identities, without
            // deleting or modifying the genuine sale or its immutable payload.
            seed.CommandText = """
                INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
                SELECT @ReportingBusiness,TenantId,N'Histórico conservado',N'',N'',N'',N'',N'' FROM dbo.Businesses WHERE BusinessId=(SELECT BusinessId FROM reporting.SalesReportDocuments WHERE DocumentId=@Sale);
                UPDATE reporting.SalesReportDocuments SET DocumentId=@HistoricalSale,BusinessId=@ReportingBusiness,ProjectionVersion=2,ProductQuantity=0,MissingCostLineCount=0 WHERE DocumentId=@Sale;
                UPDATE reporting.SalesReportLineFacts SET SourceDocumentId=CASE WHEN SourceDocumentId=@Sale THEN @HistoricalSale ELSE SourceDocumentId END,
                  OriginalSaleDocumentId=@HistoricalSale,BusinessId=@ReportingBusiness,CostAvailable=0,TaxCode=NULL,TaxRate=NULL WHERE SourceDocumentId IN(@Sale,@Return);
                UPDATE reporting.SalesReportTaxFacts SET SourceDocumentId=CASE WHEN SourceDocumentId=@Sale THEN @HistoricalSale ELSE SourceDocumentId END,
                  OriginalSaleDocumentId=CASE WHEN OriginalSaleDocumentId=@Sale THEN @HistoricalSale ELSE OriginalSaleDocumentId END,
                  BusinessId=@ReportingBusiness WHERE SourceDocumentId IN(@Sale,@Return);
                UPDATE reporting.SalesReportPaymentFacts SET SourceDocumentId=@HistoricalSale,BusinessId=@ReportingBusiness WHERE SourceDocumentId=@Sale;
                INSERT reporting.SalesReportLineFacts(FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
                  SourceLineNumber,OriginalSaleDocumentId,OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,
                  WarehouseId,CustomerId,ProductId,ProductCode,ProductName,Quantity,GrossAmount,DiscountAmount,
                  UntaxedAmount,TaxAmount,TotalAmount,RecognizedCostAmount,ProjectionVersion,ProjectedAt,CostAvailable)
                SELECT NEWID(),TenantId,BusinessId,SourceDocumentId,SourceDocumentType,n.Number,OriginalSaleDocumentId,
                  OriginalLineNumber,MovementType,OccurredAt,BusinessLocalDate,WarehouseId,CustomerId,ProductId,ProductCode,ProductName,
                  Quantity/10,GrossAmount/10,DiscountAmount/10,UntaxedAmount/10-CASE WHEN n.Number=2 THEN 0.0002 ELSE 0 END,
                  TaxAmount/10,TotalAmount/10-CASE WHEN n.Number=2 THEN 0.0002 ELSE 0 END,RecognizedCostAmount/10,2,ProjectedAt,0
                FROM reporting.SalesReportLineFacts CROSS JOIN (VALUES(2),(3),(4),(5),(6),(7),(8),(9),(10),(11)) n(Number)
                WHERE SourceDocumentId=@HistoricalSale AND SourceLineNumber=1;
                DELETE reporting.SalesReportLineFacts WHERE SourceDocumentId=@HistoricalSale AND SourceLineNumber=1;
                """;
            await seed.ExecuteNonQueryAsync();
            seed.Parameters["@Sale"].Value=reportingSale;
        }
        var protectedFacts = await ReportingMigrationFingerprintAsync(connection);
        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>();
        seed.CommandText="UPDATE reporting.SalesReportTaxFacts SET TaxableAmount=TaxableAmount-1,TotalAmount=TotalAmount-1 WHERE SourceDocumentId=@Return AND TaxCode=N'IMPORTED-A';";
        await seed.ExecuteNonQueryAsync();
        var rejected=await Assert.ThrowsAsync<SqlException>(()=>processor.RebuildAsync(fixture.TenantId,reportingBusiness,default));
        Assert.Equal(51626,rejected.Number);
        Assert.Equal(protectedFacts,await ReportingMigrationFingerprintAsync(connection));
        seed.CommandText="UPDATE reporting.SalesReportTaxFacts SET TaxableAmount=TaxableAmount+1,TotalAmount=TotalAmount+1 WHERE SourceDocumentId=@Return AND TaxCode=N'IMPORTED-A';";
        await seed.ExecuteNonQueryAsync();
        // Interrupt the final aggregate transaction. Retained evidence must
        // survive and the ordinary rebuild must finish without a recovery path.
        var trigger = "TR_TestPreservedHistory_" + Guid.NewGuid().ToString("N");
        await using var failure = new SqlCommand($"""
            CREATE TRIGGER reporting.[{trigger}] ON reporting.SalesReportDailyTotals AFTER INSERT,UPDATE AS
            IF EXISTS(SELECT 1 FROM inserted i JOIN reporting.SalesReportTaxFacts f
              ON f.BusinessId=i.BusinessId AND f.BusinessLocalDate=i.BusinessLocalDate
              WHERE f.SourceDocumentId='{returned:D}') THROW 51993,N'Interrupted reporting rehearsal',1;
            """,connection);
        await failure.ExecuteNonQueryAsync();
        try
        {
            var interrupted = await Assert.ThrowsAsync<SqlException>(()=>processor.RebuildAsync(fixture.TenantId,reportingBusiness,default));
            Assert.Equal(51993,interrupted.Number);
        }
        finally
        {
            failure.CommandText=$"DROP TRIGGER reporting.[{trigger}];";
            await failure.ExecuteNonQueryAsync();
        }
        string? importedSnapshot = null;
        for (var replay = 0; replay < 2; replay++)
        {
            await processor.RebuildAsync(fixture.TenantId, reportingBusiness, default);
            Assert.Equal(protectedFacts, await ReportingMigrationFingerprintAsync(connection));
            seed.CommandText = """
                SELECT (SELECT COUNT(*) FROM dbo.SalesReturns WHERE ReturnId=@Return),
                  (SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Return),
                  (SELECT COUNT(*) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Return AND OfficialDocumentTotalAmount IS NULL AND OriginalSaleDocumentId=@Sale),
                  (SELECT COUNT(*) FROM reporting.SalesReportLineFacts WHERE SourceDocumentId=@Return AND TaxCode IS NULL AND CostAvailable=0),
                  (SELECT SUM(UntaxedAmount) FROM reporting.SalesReportActivity(@Tenant,@Business,'20260101','20261231',@Return,NULL,0)),
                  (SELECT -UntaxedAmount FROM reporting.SalesReportDocuments WHERE DocumentId=@Sale),
                  (SELECT SUM(NetTotalSales) FROM reporting.SalesReportDailyTotals WHERE BusinessId=@Business AND BusinessLocalDate=(SELECT MIN(BusinessLocalDate) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Return)),
                  (SELECT SUM(TotalAmount) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Return);
                """;
            if (replay == 0) { seed.Parameters.AddWithValue("@Tenant", fixture.TenantId); seed.Parameters.AddWithValue("@Business", reportingBusiness); }
            await using (var data = await seed.ExecuteReaderAsync())
            {
                Assert.True(await data.ReadAsync());
                Assert.Equal(0, data.GetInt32(0)); Assert.Equal(0, data.GetInt32(1));
                Assert.Equal(2, data.GetInt32(2)); Assert.True(data.GetInt32(3) > 0);
                Assert.Equal(data.GetDecimal(5), data.GetDecimal(4));
                Assert.Equal(data.GetDecimal(7), data.GetDecimal(6));
            }
            seed.CommandText = """
                SELECT (SELECT * FROM reporting.SalesReportLineFacts WHERE SourceDocumentId=@Return ORDER BY SourceLineNumber FOR JSON PATH) Lines,
                  (SELECT * FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Return ORDER BY TaxCode FOR JSON PATH) Taxes FOR JSON PATH;
                """;
            var actual = Assert.IsType<string>(await seed.ExecuteScalarAsync());
            if (importedSnapshot is null) importedSnapshot = actual; else Assert.Equal(importedSnapshot, actual);
            seed.CommandText = """
                SELECT d.ProductQuantity,d.MissingCostLineCount,d.ProjectionVersion,
                  (SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Sale),
                  (SELECT RecognizedCostAmount FROM reporting.SalesReportActivity(@Tenant,@Business,'20260101','20261231',@Sale,NULL,1)),d.SourceDocumentAvailable
                FROM reporting.SalesReportDocuments d WHERE d.DocumentId=@Sale;
                """;
            await using var header = await seed.ExecuteReaderAsync();
            Assert.True(await header.ReadAsync()); Assert.Equal(sale.Lines.Sum(l=>l.Quantity),header.GetDecimal(0));
            Assert.Equal(historicalSale?10:0,header.GetInt64(1));
            Assert.Equal(SqlSalesReportingProjectionWriter.ProjectionVersion,header.GetInt16(2));
            Assert.Equal(historicalSale?0:1,header.GetInt32(3));
            Assert.Equal(historicalSale,header.IsDBNull(4));
            Assert.Equal(!historicalSale,header.GetBoolean(5));
            await header.DisposeAsync();
            var reports=scope.ServiceProvider.GetRequiredService<ISalesReportingStore>();
            var actor=new SalesReportingUserIdentity(fixture.UserId,fixture.TenantId,reportingBusiness,
                new HashSet<string>{SalesReportingPermissionCodes.Read,SalesReportingPermissionCodes.ReadAll});
            var filter=new SalesReportFilter(new(2026,1,1),new(2026,12,31));
            var context=await reports.GetContextAsync(actor,default);
            Assert.True(context.HistoricalReturnsWithoutOfficialTotal>0);
            var detail=await reports.ExploreAsync(actor,new(filter,DocumentId:reportingSale),default);
            Assert.Null(detail.OfficialTotal); Assert.Null(detail.RoundingAdjustment);
            Assert.Equal(!historicalSale,detail.Document!.SourceDocumentAvailable);
        }
    }

    [Theory]
    [InlineData(0)] // Existing job without its source reference.
    [InlineData(1)] // Historical table before the source columns existed.
    [InlineData(2)] // Historical completed operation without a reporting job.
    public async Task Reporting_source_upgrade_preserves_history_and_rebuilds_once(int historicalShape)
    {
        var sale=fixture.CreateValidRequest(9_560+historicalShape);
        using(var upload=fixture.CreateUploadMessage(sale))
        using(var response=await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        var before=await ReportingMigrationFingerprintAsync(connection);
        await using var command=connection.CreateCommand();command.Parameters.AddWithValue("@Id",sale.DocumentId);
        command.CommandText=historicalShape switch {
            0=>"ALTER TABLE reporting.SalesReportingJobs DROP CONSTRAINT CK_SalesReportingJobs_SourceShape; UPDATE reporting.SalesReportingJobs SET SourceDocumentProcessingJobId=NULL WHERE SourceDocumentId=@Id;",
            1=>"ALTER TABLE reporting.SalesReportingJobs DROP CONSTRAINT CK_SalesReportingJobs_SourceShape,FK_SalesReportingJobs_Source; ALTER TABLE reporting.SalesReportingJobs DROP COLUMN SourceDocumentProcessingJobId,SourcePayloadJson;",
            _=>"DELETE reporting.SalesReportingJobs WHERE SourceDocumentId=@Id;"
        };
        await command.ExecuteNonQueryAsync();
        try
        {
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            await fixture.ApplyMigrationAsync("20261121_BackfillReportingSourceReferences.sql",connection);
            Assert.True(elapsed.Elapsed<TimeSpan.FromSeconds(5),$"Source migration exceeded its local budget: {elapsed.Elapsed}.");
            await fixture.ReapplySchemaAsync();
            command.CommandText="SELECT CONVERT(varchar(36),SourceDocumentProcessingJobId)+'|'+CONVERT(varchar(18),RowVersion,1) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Id;";
            var reference=Assert.IsType<string>(await command.ExecuteScalarAsync());
            Assert.NotEmpty(reference.Split('|')[0]);
            await fixture.ApplyMigrationAsync("20261121_BackfillReportingSourceReferences.sql",connection);
            Assert.Equal(reference,await command.ExecuteScalarAsync());
            Assert.Equal(before,await ReportingMigrationFingerprintAsync(connection));

            // SQL CHECK must reject both-null sources, rather than accept UNKNOWN.
            command.CommandText="UPDATE reporting.SalesReportingJobs SET SourceDocumentProcessingJobId=NULL,SourcePayloadJson=NULL WHERE SourceDocumentId=@Id;";
            var rejected=await Assert.ThrowsAsync<SqlException>(()=>command.ExecuteNonQueryAsync());Assert.Equal(547,rejected.Number);
            using var scope=fixture.CreateScope();var processor=scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>();
            for(var replay=0;replay<2;replay++)
            {
                await processor.RebuildAsync(fixture.TenantId,fixture.BusinessId,default);
                command.CommandText="SELECT COUNT(*) FROM reporting.SalesReportDocuments WHERE DocumentId=@Id;";
                Assert.Equal(1,Convert.ToInt32(await command.ExecuteScalarAsync()));
                Assert.Equal(before,await ReportingMigrationFingerprintAsync(connection));
            }
        }
        finally
        {
            await fixture.ApplyMigrationAsync("20261121_BackfillReportingSourceReferences.sql",connection);
            await fixture.ReapplySchemaAsync();
        }
    }

    [Fact]
    public async Task Reporting_source_upgrade_rejects_an_unverifiable_hash_without_partial_changes()
    {
        var sales=new[]{fixture.CreateValidRequest(9_563),fixture.CreateValidRequest(9_564)};
        foreach(var sale in sales)
        {
            using var upload=fixture.CreateUploadMessage(sale);using var accepted=await fixture.CreateClient().SendAsync(upload);
            Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
        }
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        var before=await ReportingMigrationFingerprintAsync(connection);
        await using var command=connection.CreateCommand();command.Parameters.AddWithValue("@First",sales[0].DocumentId);command.Parameters.AddWithValue("@Second",sales[1].DocumentId);
        command.CommandText="""
            ALTER TABLE reporting.SalesReportingJobs DROP CONSTRAINT CK_SalesReportingJobs_SourceShape;
            UPDATE reporting.SalesReportingJobs SET SourceDocumentProcessingJobId=NULL WHERE SourceDocumentId IN(@First,@Second);
            UPDATE reporting.SalesReportingJobs SET SourcePayloadHash=HASHBYTES('SHA2_256',N'unverified-test-source') WHERE SourceDocumentId=@Second;
            """;
        await command.ExecuteNonQueryAsync();
        try
        {
            var rejected=await Assert.ThrowsAsync<SqlException>(()=>fixture.ApplyMigrationAsync("20261121_BackfillReportingSourceReferences.sql",connection));
            Assert.Equal(51623,rejected.Number);
            command.CommandText="SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId IN(@First,@Second) AND SourceDocumentProcessingJobId IS NULL;";
            Assert.Equal(2,Convert.ToInt32(await command.ExecuteScalarAsync()));
            Assert.Equal(before,await ReportingMigrationFingerprintAsync(connection));
        }
        finally
        {
            command.CommandText="""
                UPDATE report SET SourcePayloadHash=payload.PayloadHash FROM reporting.SalesReportingJobs report
                JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=report.SourceDocumentId
                  AND payload.DocumentType=report.SourceDocumentType AND payload.BusinessId=report.BusinessId
                WHERE report.SourceDocumentId IN(@First,@Second);
                """;
            await command.ExecuteNonQueryAsync();
            await fixture.ApplyMigrationAsync("20261121_BackfillReportingSourceReferences.sql",connection);
            await fixture.ReapplySchemaAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reporting_source_upgrade_restores_only_the_verified_original_snapshot(bool changedOperationalHash)
    {
        var sale=fixture.CreateValidRequest(changedOperationalHash?9_572:9_571);
        using(var upload=fixture.CreateUploadMessage(sale))
        using(var accepted=await fixture.CreateClient().SendAsync(upload))
            Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var command=connection.CreateCommand();command.Parameters.AddWithValue("@Id",sale.DocumentId);
        command.CommandText="SELECT p.PayloadJson,p.PayloadHash,a.PayloadJson,a.PayloadHash,r.SalesReportingJobId FROM dbo.DocumentProcessingPayloads p JOIN dbo.AccountingSourceDocuments a ON a.SourceDocumentId=p.DocumentId AND a.SourceDocumentType=p.DocumentType JOIN reporting.SalesReportingJobs r ON r.SourceDocumentId=p.DocumentId AND r.SourceDocumentType=p.DocumentType WHERE p.DocumentId=@Id;";
        string original;byte[] hash;Guid job;
        await using(var reader=await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());original=reader.GetString(0);hash=(byte[])reader[1];job=reader.GetGuid(4);
            Assert.Equal(original,reader.GetString(2));Assert.Equal(hash,(byte[])reader[3]);
            Assert.Equal(hash,SHA256.HashData(Encoding.UTF8.GetBytes(original)));
        }
        command.Parameters.AddWithValue("@Original",original);command.Parameters.AddWithValue("@Hash",hash);
        var changed=original+" ";command.Parameters.AddWithValue("@Changed",changed);
        command.Parameters.AddWithValue("@ChangedHash",changedOperationalHash?SHA256.HashData(Encoding.UTF8.GetBytes(changed)):hash);
        command.CommandText="UPDATE dbo.DocumentProcessingPayloads SET PayloadJson=@Changed,PayloadHash=@ChangedHash WHERE DocumentId=@Id;";
        await command.ExecuteNonQueryAsync();
        var protectedFacts=await ReportingMigrationFingerprintAsync(connection);
        try
        {
            string? retained=null;
            for(var pass=0;pass<2;pass++)
            {
                await fixture.ApplyMigrationAsync("20261121_BackfillReportingSourceReferences.sql",connection);
                command.CommandText="SELECT SalesReportingJobId,SourceDocumentProcessingJobId,SourcePayloadJson,SourcePayloadHash,CONVERT(varchar(18),RowVersion,1) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Id;";
                await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
                Assert.Equal(job,reader.GetGuid(0));Assert.True(reader.IsDBNull(1));Assert.Equal(original,reader.GetString(2));Assert.Equal(hash,(byte[])reader[3]);
                if(retained is null)retained=reader.GetString(4);else Assert.Equal(retained,reader.GetString(4));
            }
            Assert.Equal(protectedFacts,await ReportingMigrationFingerprintAsync(connection));
            using var scope=fixture.CreateScope();var processor=scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>();
            for(var replay=0;replay<2;replay++)
            {
                await processor.RebuildAsync(fixture.TenantId,fixture.BusinessId,default);
                Assert.Equal(protectedFacts,await ReportingMigrationFingerprintAsync(connection));
                command.CommandText="SELECT (SELECT COUNT(*) FROM reporting.SalesReportDocuments WHERE DocumentId=@Id),(SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Id AND Status=N'Projected'),(SELECT PayloadJson FROM dbo.DocumentProcessingPayloads WHERE DocumentId=@Id),(SELECT PayloadJson FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id);";
                await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
                Assert.Equal(1,reader.GetInt32(0));Assert.Equal(1,reader.GetInt32(1));Assert.Equal(changed,reader.GetString(2));Assert.Equal(original,reader.GetString(3));
            }
        }
        finally
        {
            command.CommandText="UPDATE dbo.DocumentProcessingPayloads SET PayloadJson=@Original,PayloadHash=@Hash WHERE DocumentId=@Id;";
            await command.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("documentId")]
    [InlineData("tenantId")]
    [InlineData("businessId")]
    [InlineData("documentType")]
    [InlineData("hash")]
    public async Task Reporting_source_integrity_rejects_unverified_archives_before_reset(string invalidField)
    {
        var sale=fixture.CreateValidRequest(9_580+Array.IndexOf(new[]{"documentId","tenantId","businessId","documentType","hash"},invalidField));
        using(var upload=fixture.CreateUploadMessage(sale))
        using(var accepted=await fixture.CreateClient().SendAsync(upload))
            Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var command=connection.CreateCommand();command.Parameters.AddWithValue("@Id",sale.DocumentId);
        command.CommandText="SELECT PayloadJson,PayloadHash FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id;";
        string original;byte[] originalHash;
        await using(var reader=await command.ExecuteReaderAsync())
        {Assert.True(await reader.ReadAsync());original=reader.GetString(0);originalHash=(byte[])reader[1];}
        var node=JsonNode.Parse(original)!.AsObject();
        if(invalidField=="documentType")node["commercialSnapshot"]!["documentType"]="SalesReceipt";
        else if(invalidField!="hash")node[invalidField]=Guid.NewGuid();
        var changed=node.ToJsonString();var changedHash=SHA256.HashData(Encoding.UTF8.GetBytes(changed));
        if(invalidField=="hash")changedHash[0]^=0xff;
        command.Parameters.AddWithValue("@Original",original);command.Parameters.AddWithValue("@OriginalHash",originalHash);
        command.Parameters.AddWithValue("@Changed",changed);command.Parameters.AddWithValue("@ChangedHash",changedHash);
        command.CommandText="UPDATE dbo.AccountingSourceDocuments SET PayloadJson=@Changed,PayloadHash=@ChangedHash WHERE SourceDocumentId=@Id;UPDATE reporting.SalesReportingJobs SET SourcePayloadHash=@ChangedHash WHERE SourceDocumentId=@Id;";
        await command.ExecuteNonQueryAsync();
        var effects=await ReportingMigrationFingerprintAsync(connection);var reports=await ReportingReadModelFingerprintAsync(connection);
        using var scope=fixture.CreateScope();var processor=scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>();
        try
        {
            var migration=await Assert.ThrowsAsync<SqlException>(()=>fixture.ApplyMigrationAsync("20261121_BackfillReportingSourceReferences.sql",connection));
            Assert.Equal(51629,migration.Number);
            Assert.Equal(reports,await ReportingReadModelFingerprintAsync(connection));
            var rebuild=await Assert.ThrowsAsync<SqlException>(()=>processor.RebuildAsync(fixture.TenantId,fixture.BusinessId,default));
            Assert.Equal(51629,rebuild.Number);
            Assert.Equal(reports,await ReportingReadModelFingerprintAsync(connection));
            Assert.Equal(effects,await ReportingMigrationFingerprintAsync(connection));
        }
        finally
        {
            command.CommandText="UPDATE dbo.AccountingSourceDocuments SET PayloadJson=@Original,PayloadHash=@OriginalHash WHERE SourceDocumentId=@Id;UPDATE reporting.SalesReportingJobs SET SourcePayloadHash=@OriginalHash WHERE SourceDocumentId=@Id;";
            await command.ExecuteNonQueryAsync();
            await processor.RebuildAsync(fixture.TenantId,fixture.BusinessId,default);
        }
    }

    [Fact]
    public async Task Reporting_integrity_hash_uses_the_same_utf8_bytes_in_sql_and_the_processor()
    {
        const string payload="José, Bogotá, ñ, 中文, 🙂";
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var command=new SqlCommand("SELECT HASHBYTES('SHA2_256',CONVERT(varchar(max),@Payload COLLATE Latin1_General_100_BIN2_UTF8));",connection);
        command.Parameters.Add("@Payload",System.Data.SqlDbType.NVarChar,-1).Value=payload;
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(payload)),Assert.IsType<byte[]>(await command.ExecuteScalarAsync()));
    }

    private async Task<string> ReportingReadModelFingerprintAsync(SqlConnection connection)
    {
        await using var command=connection.CreateCommand();command.Parameters.AddWithValue("@Business",fixture.BusinessId);
        command.CommandText="""
            SELECT (SELECT
              (SELECT * FROM reporting.SalesReportDocuments WHERE BusinessId=@Business ORDER BY DocumentId FOR JSON PATH) Documents,
              (SELECT * FROM reporting.SalesReportLineFacts WHERE BusinessId=@Business ORDER BY FactId FOR JSON PATH) Lines,
              (SELECT * FROM reporting.SalesReportTaxFacts WHERE BusinessId=@Business ORDER BY SourceDocumentId,SourceDocumentType,TaxCode,TaxRate FOR JSON PATH) Taxes,
              (SELECT * FROM reporting.SalesReportPaymentFacts WHERE BusinessId=@Business ORDER BY SourceDocumentId,SourceDocumentType,PaymentNumber FOR JSON PATH) Payments,
              (SELECT * FROM reporting.SalesReportingJobs WHERE BusinessId=@Business ORDER BY SalesReportingJobId FOR JSON PATH) Jobs,
              (SELECT * FROM reporting.SalesReportingCheckpoints WHERE BusinessId=@Business ORDER BY ProjectionVersion FOR JSON PATH) Checkpoints,
              (SELECT * FROM reporting.SalesReportDailyTotals WHERE BusinessId=@Business ORDER BY BusinessLocalDate,CurrencyCode FOR JSON PATH) Daily
              FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Assert.IsType<string>(await command.ExecuteScalarAsync()))));
    }

    private async Task<string> ReportingMigrationFingerprintAsync(SqlConnection connection)
    {
        await using var command=connection.CreateCommand();command.Parameters.AddWithValue("@Business",fixture.BusinessId);
        command.CommandText="""
            SELECT Kind,Rows,Digest FROM (
              SELECT N'Sales' Kind,COUNT_BIG(*) Rows,CHECKSUM_AGG(BINARY_CHECKSUM(*)) Digest FROM dbo.SalesDocuments WHERE BusinessId=@Business
              UNION ALL SELECT N'Payloads',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.DocumentProcessingPayloads WHERE BusinessId=@Business
              UNION ALL SELECT N'Operations',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.DocumentProcessingJobs WHERE BusinessId=@Business
              UNION ALL SELECT N'Movements',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.InventoryMovements WHERE BusinessId=@Business
              UNION ALL SELECT N'Balances',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.InventoryBalances WHERE BusinessId=@Business
              UNION ALL SELECT N'Receivables',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.Receivables WHERE BusinessId=@Business
              UNION ALL SELECT N'Payables',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.Payables WHERE BusinessId=@Business
              UNION ALL SELECT N'Entries',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.AccountingEntries WHERE BusinessId=@Business
            ) snapshots ORDER BY Kind FOR JSON PATH;
            """;
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_null_uploaded_estimate_uses_the_cost_already_frozen_on_the_invoice(bool generic)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var sale = fixture.CreateValidRequest(generic ? 9_599 : 9_598);
        bool wasGeneric = false, managedStock = true;
        if (generic)
        {
            await using var configure = connection.CreateCommand();
            configure.CommandText = """
                UPDATE dbo.Products SET IsGenericProduct=1,ManageStock=0
                  OUTPUT deleted.IsGenericProduct,deleted.ManageStock WHERE ProductId=@Product;
                """;
            configure.Parameters.AddWithValue("@Product", fixture.ProductId);
            await using var state = await configure.ExecuteReaderAsync();
            Assert.True(await state.ReadAsync());
            wasGeneric = state.GetBoolean(0); managedStock = state.GetBoolean(1);
            sale = sale with { Lines = sale.Lines.Select(line => line with {
                IsGenericProductSnapshot = true, DocumentUnitCost = 6_000m }).ToArray() };
        }
        try
        {
            using var upload = fixture.CreateUploadMessage(sale);
            using var response = await fixture.CreateClient().SendAsync(upload);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            if (generic)
            {
                await using var restore = connection.CreateCommand();
                restore.CommandText = "UPDATE dbo.Products SET IsGenericProduct=@Generic,ManageStock=@Stock WHERE ProductId=@Product;";
                restore.Parameters.AddWithValue("@Generic", wasGeneric); restore.Parameters.AddWithValue("@Stock", managedStock);
                restore.Parameters.AddWithValue("@Product", fixture.ProductId);
                await restore.ExecuteNonQueryAsync();
            }
        }
        await using var source = connection.CreateCommand();
        source.CommandText = """
            SELECT payload.PayloadJson FROM reporting.SalesReportingJobs report
            JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=report.SourceDocumentId
              AND payload.DocumentType=report.SourceDocumentType AND payload.BusinessId=report.BusinessId
            WHERE report.SourceDocumentId=@Document;
            """;
        source.Parameters.AddWithValue("@Document", sale.DocumentId);
        var legacy = JsonNode.Parse(Assert.IsType<string>(await source.ExecuteScalarAsync()))!;
        foreach (var line in legacy["lines"]!.AsArray()) line!["documentUnitCost"] = null;
        var payload = legacy.ToJsonString();
        Assert.Throws<JsonException>(() => PosSaleContractSerializer.Deserialize(payload));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        // Simulate an accepted historical source only in this disposable fixture.
        // Do not alter inventory: the generic sale naturally has no ledger entry.
        source.CommandText = """
            UPDATE payload SET PayloadJson=@Payload,PayloadHash=@Hash FROM dbo.DocumentProcessingPayloads payload
            JOIN reporting.SalesReportingJobs report ON report.SourceDocumentId=payload.DocumentId
              AND report.SourceDocumentType=payload.DocumentType AND report.BusinessId=payload.BusinessId
            WHERE report.SourceDocumentId=@Document;
            UPDATE reporting.SalesReportingJobs SET SourcePayloadHash=@Hash WHERE SourceDocumentId=@Document;
            UPDATE dbo.SalesDocuments SET PayloadHash=@Hash WHERE DocumentId=@Document;
            """;
        source.Parameters.AddWithValue("@Payload", payload); source.Parameters.AddWithValue("@Hash", hash);
        await source.ExecuteNonQueryAsync();
        var originalEffects = await ReportingMigrationFingerprintAsync(connection);
        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>();
        for (var replay = 0; replay < 2; replay++)
        {
            await processor.RebuildAsync(fixture.TenantId, fixture.BusinessId, default);
            await using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT fact.CostAvailable,report.RecognizedCostAmount,report.MissingCostLineCount,
                  (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Document),
                  (SELECT -SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Document AND MovementType=N'Sale'),
                  payload.PayloadJson,payload.PayloadHash,
                  CONVERT(bit,IIF(report.TotalAmount=sale.PayableAmount AND report.UntaxedAmount=sale.UntaxedAmount
                    AND report.TaxAmount=sale.TaxAmount,1,0))
                FROM reporting.SalesReportDocuments report
                JOIN reporting.SalesReportLineFacts fact ON fact.SourceDocumentId=report.DocumentId
                JOIN dbo.SalesDocuments sale ON sale.DocumentId=report.DocumentId
                JOIN reporting.SalesReportingJobs source ON source.SourceDocumentId=report.DocumentId
                JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=source.SourceDocumentId
                  AND payload.DocumentType=source.SourceDocumentType AND payload.BusinessId=source.BusinessId
                WHERE report.DocumentId=@Document;
                """;
            verify.Parameters.AddWithValue("@Document", sale.DocumentId);
            await using var result = await verify.ExecuteReaderAsync();
            Assert.True(await result.ReadAsync()); Assert.True(result.GetBoolean(0));
            if (generic) Assert.Equal(6_000m * sale.Lines[0].Quantity, result.GetDecimal(1));
            else Assert.Equal(result.GetDecimal(4), result.GetDecimal(1));
            Assert.Equal(0L, result.GetInt64(2));
            Assert.Equal(generic ? 0 : 1, result.GetInt32(3));
            Assert.Equal(payload, result.GetString(5)); Assert.Equal(hash, (byte[])result.GetValue(6));
            Assert.True(result.GetBoolean(7)); Assert.False(await result.ReadAsync());
            await result.DisposeAsync();
            Assert.Equal(originalEffects, await ReportingMigrationFingerprintAsync(connection));
            await using var untouched = connection.CreateCommand();
            untouched.CommandText = "SELECT COUNT(*) FROM dbo.SalesDocumentLines WHERE DocumentId=@Document;";
            untouched.Parameters.AddWithValue("@Document", sale.DocumentId);
            Assert.Equal(sale.Lines.Count, Convert.ToInt32(await untouched.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task Recognized_sale_cost_comes_from_the_canonical_ledger_instead_of_a_stale_uploaded_estimate()
    {
        var original=fixture.CreateValidRequest(9_597);
        var sale=original with {Lines=original.Lines.Select(line=>line with {DocumentUnitCost=1m}).ToArray()};
        using(var upload=fixture.CreateUploadMessage(sale))
        using(var response=await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await using var connection=new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var query=connection.CreateCommand();query.CommandText="""
            SELECT movement.ValueChange,line.UnitCostSnapshot,fact.RecognizedCostAmount,document.RecognizedCostAmount,
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Document)
            FROM dbo.SalesDocumentLines line
            JOIN dbo.InventoryMovements movement ON movement.DocumentId=line.DocumentId AND movement.LineNumber=line.LineNumber AND movement.MovementType=N'Sale'
            JOIN reporting.SalesReportLineFacts fact ON fact.SourceDocumentId=line.DocumentId AND fact.SourceLineNumber=line.LineNumber
            JOIN reporting.SalesReportDocuments document ON document.DocumentId=line.DocumentId
            WHERE line.DocumentId=@Document;
            """;
        query.Parameters.AddWithValue("@Document",sale.DocumentId);
        await using var data=await query.ExecuteReaderAsync();Assert.True(await data.ReadAsync());
        var actual=-data.GetDecimal(0);Assert.NotEqual(sale.Lines[0].Quantity,actual);
        Assert.Equal(actual,data.GetDecimal(1)*sale.Lines[0].Quantity);
        Assert.Equal(actual,data.GetDecimal(2));Assert.Equal(actual,data.GetDecimal(3));Assert.Equal(1,data.GetInt32(4));
    }

}
