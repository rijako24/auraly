using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auraly.Contracts.Sales;
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Historical_null_uploaded_cost_preserves_ledger_cost_or_explicitly_unknown_generic_cost(bool generic, bool missingOperationalLines)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var sale = fixture.CreateValidRequest((generic ? 9_599 : 9_598) - (missingOperationalLines ? 10 : 0));
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
            -- Historical lines predate the recognized-cost column as well.
            UPDATE dbo.SalesDocumentLines SET RecognizedCostAmount=NULL WHERE DocumentId=@Document;
            IF @Generic=1 UPDATE dbo.SalesDocumentLines SET UnitCostSnapshot=NULL WHERE DocumentId=@Document;
            IF @MissingLines=1 DELETE dbo.SalesDocumentLines WHERE DocumentId=@Document;
            """;
        source.Parameters.AddWithValue("@Payload", payload); source.Parameters.AddWithValue("@Hash", hash);
        source.Parameters.AddWithValue("@Generic", generic);
        source.Parameters.AddWithValue("@MissingLines", missingOperationalLines);
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
            Assert.True(await result.ReadAsync()); Assert.Equal(!generic, result.GetBoolean(0));
            if (generic) Assert.True(result.IsDBNull(1));
            else Assert.Equal(result.GetDecimal(4), result.GetDecimal(1));
            Assert.Equal(generic ? 1L : 0L, result.GetInt64(2));
            Assert.Equal(generic ? 0 : 1, result.GetInt32(3));
            Assert.Equal(payload, result.GetString(5)); Assert.Equal(hash, (byte[])result.GetValue(6));
            Assert.True(result.GetBoolean(7)); Assert.False(await result.ReadAsync());
            await result.DisposeAsync();
            Assert.Equal(originalEffects, await ReportingMigrationFingerprintAsync(connection));
            await using var untouched = connection.CreateCommand();
            untouched.CommandText = "SELECT COUNT(*) FROM dbo.SalesDocumentLines WHERE DocumentId=@Document;";
            untouched.Parameters.AddWithValue("@Document", sale.DocumentId);
            Assert.Equal(missingOperationalLines ? 0 : sale.Lines.Count, Convert.ToInt32(await untouched.ExecuteScalarAsync()));
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
