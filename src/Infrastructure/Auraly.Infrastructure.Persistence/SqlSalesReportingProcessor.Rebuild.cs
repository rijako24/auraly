using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingProcessor
{
    public async Task<long> RebuildAsync(Guid tenantId,Guid businessId,CancellationToken cancellationToken)
    {
        if(tenantId==Guid.Empty||businessId==Guid.Empty)throw new ArgumentException("Tenant y sede son obligatorios.");
        await using var connection=connections.Create();await connection.OpenAsync(cancellationToken);
        await using(var authorize=new SqlCommand("SELECT COUNT(*) FROM dbo.Businesses WHERE BusinessId=@Business AND TenantId=@Tenant;",connection))
        {
            authorize.Parameters.AddWithValue("@Business",businessId);authorize.Parameters.AddWithValue("@Tenant",tenantId);
            if(Convert.ToInt32(await authorize.ExecuteScalarAsync(cancellationToken))!=1)
                throw new InvalidOperationException("La sede no pertenece al tenant seleccionado.");
        }
        // The session lock serializes only Reporting. Operational documents remain writable.
        await AcquireReportingLockAsync(connection,null,businessId,"Exclusive","Session",cancellationToken);
        try
        {
            // Create outside the parameterized reset batch: a temporary table
            // created inside sp_executesql would disappear before the page read.
            await using(var create=new SqlCommand("""
                CREATE TABLE #RebuildSources(Sequence bigint NOT NULL PRIMARY KEY,
                  SourceDocumentId uniqueidentifier NOT NULL,SourceDocumentType nvarchar(32) COLLATE DATABASE_DEFAULT NOT NULL,SourceVersion bigint NOT NULL);
                """,connection))
                await create.ExecuteNonQueryAsync(cancellationToken);
            await using(var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellationToken))
            {
                await SqlSalesReportingJobWriter.BackfillServiceInvoicesAsync(connection,transaction,businessId,ids,timeProvider.GetUtcNow(),cancellationToken);
                await using var reset=new SqlCommand("""
                    INSERT #RebuildSources(Sequence,SourceDocumentId,SourceDocumentType,SourceVersion)
                    SELECT ROW_NUMBER() OVER(ORDER BY CASE WHEN r.SourceDocumentType IN(N'SalesReturn',N'PurchaseReturn') THEN 1 ELSE 0 END,
                      r.CreatedAt,r.SourceDocumentId,r.SourceDocumentType,r.SourceVersion) Sequence,
                      r.SourceDocumentId,r.SourceDocumentType,r.SourceVersion
                    FROM reporting.SalesReportingJobs r
                    LEFT JOIN dbo.DocumentProcessingJobs j ON j.JobId=r.SourceDocumentProcessingJobId
                    WHERE r.BusinessId=@Business AND (r.SourcePayloadJson IS NOT NULL OR j.Status=N'Completed');
                    IF NOT EXISTS(SELECT 1 FROM #RebuildSources) THROW 51621,N'La sede no tiene fuentes inmutables para reconstruir.',1;
                    DELETE reporting.SalesReportPaymentFacts WHERE BusinessId=@Business;
                    DELETE reporting.SalesReportTaxFacts WHERE BusinessId=@Business;
                    DELETE reporting.ServiceSalesReportLineFacts WHERE BusinessId=@Business;
                    DELETE reporting.SalesReportLineFacts WHERE BusinessId=@Business;
                    DELETE reporting.SalesReportDocuments WHERE BusinessId=@Business;
                    DELETE reporting.SalesReportDailyTotals WHERE BusinessId=@Business;
                    DELETE reporting.SalesReportDailyDimensionTotals WHERE BusinessId=@Business;
                    DELETE reporting.PurchaseReportLineFacts WHERE BusinessId=@Business;
                    DELETE reporting.PurchaseReportDocuments WHERE BusinessId=@Business;
                    DELETE reporting.ProductDailyRotationSnapshots WHERE BusinessId=@Business;
                    DELETE reporting.ProductRotationSnapshots WHERE BusinessId=@Business;
                    DELETE reporting.SalesReportingCheckpoints WHERE BusinessId=@Business;
                    INSERT reporting.SalesReportingCheckpoints(BusinessId,ProjectionVersion,LastSourceDocumentId,LastSourceDocumentType,LastProjectedAt,RebuildInProgress)
                    SELECT TOP(1) @Business,@Version,SourceDocumentId,SourceDocumentType,@Now,1 FROM #RebuildSources ORDER BY Sequence;
                    UPDATE r SET Status=N'Pending',StartedAt=NULL,CompletedAt=NULL,LastError=NULL
                    FROM reporting.SalesReportingJobs r JOIN #RebuildSources s ON s.SourceDocumentId=r.SourceDocumentId
                      AND s.SourceDocumentType=r.SourceDocumentType AND s.SourceVersion=r.SourceVersion WHERE r.BusinessId=@Business;
                    """,connection,transaction);
                reset.Parameters.AddWithValue("@Business",businessId);reset.Parameters.AddWithValue("@Version",SqlSalesReportingProjectionWriter.ProjectionVersion);
                reset.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());reset.CommandTimeout=120;
                await reset.ExecuteNonQueryAsync(cancellationToken);await transaction.CommitAsync(cancellationToken);
            }
            long cursor=0;
            while(true)
            {
                var batch=new List<(long Sequence,Guid Document,string Type,long Version)>();
                await using(var read=new SqlCommand("SELECT TOP(200) Sequence,SourceDocumentId,SourceDocumentType,SourceVersion FROM #RebuildSources WHERE Sequence>@After ORDER BY Sequence;",connection))
                {
                    read.Parameters.AddWithValue("@After",cursor);
                    await using var reader=await read.ExecuteReaderAsync(cancellationToken);
                    while(await reader.ReadAsync(cancellationToken))batch.Add((reader.GetInt64(0),reader.GetGuid(1),reader.GetString(2),reader.GetInt64(3)));
                }
                if(batch.Count==0)break;
                // Every item is an independent canonical Reporting transaction. No operational handler is invoked.
                foreach(var source in batch)
                {
                    await ProcessSourceAsync(connection,source.Document,source.Type,businessId,source.Version,true,cancellationToken);
                    cursor=source.Sequence;
                }
            }
            await using var complete=new SqlCommand("UPDATE reporting.SalesReportingCheckpoints SET RebuildInProgress=0 WHERE BusinessId=@Business AND ProjectionVersion=@Version;",connection);
            complete.Parameters.AddWithValue("@Business",businessId);complete.Parameters.AddWithValue("@Version",SqlSalesReportingProjectionWriter.ProjectionVersion);
            if(await complete.ExecuteNonQueryAsync(cancellationToken)!=1)throw new InvalidOperationException("La reconstrucción no pudo cerrar su checkpoint.");
            return cursor;
        }
        finally
        {
            await using var release=new SqlCommand("EXEC sys.sp_releaseapplock @Resource,@LockOwner=N'Session';",connection);
            release.Parameters.AddWithValue("@Resource",ReportingLockResource(businessId));
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    internal const string TransactionLockSql="""
        DECLARE @ReportingLockResult int;
        EXEC @ReportingLockResult=sys.sp_getapplock @Resource=@ReportingLockResource,@LockMode=@ReportingLockMode,@LockOwner=N'Transaction',@LockTimeout=5000;
        IF @ReportingLockResult<0 THROW 51620,N'La proyección está ocupada; vuelve a intentar cuando termine la operación en curso.',1;
        """;

    internal static string ReportingLockResource(Guid businessId)=>$"reporting:{businessId:D}";
    private static async Task AcquireReportingLockAsync(SqlConnection connection,SqlTransaction? transaction,
        Guid businessId,string mode,string owner,CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource,@LockMode=@Mode,@LockOwner=@Owner,@LockTimeout=5000;
            IF @Result<0 THROW 51620,N'La proyección está ocupada; vuelve a intentar cuando termine la operación en curso.',1;
            """,connection,transaction);
        command.Parameters.AddWithValue("@Resource",ReportingLockResource(businessId));command.Parameters.AddWithValue("@Mode",mode);command.Parameters.AddWithValue("@Owner",owner);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
