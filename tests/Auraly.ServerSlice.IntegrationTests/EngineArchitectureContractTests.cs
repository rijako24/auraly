using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Architecture")]
public sealed class EngineArchitectureContractTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Every_engine_has_one_owned_work_table_and_derived_sources_are_decoupled()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              CASE WHEN OBJECT_ID(N'dbo.DocumentProcessingJobs', N'U') IS NULL THEN 0 ELSE 1 END,
              CASE WHEN OBJECT_ID(N'dbo.AccountingPostingJobs', N'U') IS NULL THEN 0 ELSE 1 END,
              CASE WHEN OBJECT_ID(N'dbo.FiscalDocumentProcesses', N'U') IS NULL THEN 0 ELSE 1 END,
              CASE WHEN OBJECT_ID(N'reporting.SalesReportingJobs', N'U') IS NULL THEN 0 ELSE 1 END,
              CASE WHEN OBJECT_ID(N'dbo.AccountingSourceDocuments', N'U') IS NULL THEN 0 ELSE 1 END,
              (SELECT COUNT_BIG(*)
               FROM sys.foreign_keys
               WHERE parent_object_id=OBJECT_ID(N'dbo.AccountingPostingJobs')
                 AND referenced_object_id=OBJECT_ID(N'dbo.DocumentProcessingJobs')),
              (SELECT COUNT_BIG(*)
               FROM sys.foreign_keys
               WHERE parent_object_id=OBJECT_ID(N'reporting.SalesReportingJobs')
                 AND referenced_object_id=OBJECT_ID(N'dbo.DocumentProcessingJobs'));
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
        Assert.Equal(0L, reader.GetInt64(5));
        Assert.Equal(1L, reader.GetInt64(6));
    }
}
